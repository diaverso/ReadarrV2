using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentValidation.Results;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Download.Clients.HttpBlackhole
{
    public class HttpBlackhole : DownloadClientBase<HttpBlackholeSettings>
    {
        private readonly IHttpClient _httpClient;

        public HttpBlackhole(IHttpClient httpClient,
            IConfigService configService,
            IDiskProvider diskProvider,
            IRemotePathMappingService remotePathMappingService,
            Logger logger)
            : base(configService, diskProvider, remotePathMappingService, logger)
        {
            _httpClient = httpClient;
        }

        public override string Name => "HTTP Download";

        public override DownloadProtocol Protocol => DownloadProtocol.Unknown;

        public override async Task<string> Download(RemoteBook remoteBook, IIndexer indexer)
        {
            var url = remoteBook.Release.DownloadUrl;
            if (url.IsNullOrWhiteSpace())
            {
                throw new DownloadClientException("No download URL provided for release '{0}'", remoteBook.Release.Title);
            }

            var customHeaders = remoteBook.Release.CustomDownloadHeaders;

            HttpResponse response;
            try
            {
                response = await FetchWithHeaders(url, customHeaders);
            }
            catch (Exception ex)
            {
                throw new DownloadClientException("Failed to download '{0}': {1}", remoteBook.Release.Title, ex.Message);
            }

            if (response.HasHttpError)
            {
                throw new DownloadClientException("Server returned status {0} for '{1}'", response.StatusCode, url);
            }

            // Follow the resolution chain: JSON metadata → HTML page → binary book file.
            // Re-evaluate content-type at every step so we handle multi-hop responses correctly.
            var currentUrl = url;
            for (var step = 0; step < 4; step++)
            {
                var ct = response.Headers.ContentType ?? string.Empty;

                if (ct.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                    ct.Contains("javascript", StringComparison.OrdinalIgnoreCase))
                {
                    var nextUrl = TryExtractDownloadUrlFromJson(response, currentUrl);
                    if (string.IsNullOrWhiteSpace(nextUrl))
                    {
                        break; // JSON didn't have a download URL — nothing to follow
                    }

                    _logger.Debug("Resolved JSON download URL for '{0}': {1}", remoteBook.Release.Title, nextUrl);
                    currentUrl = nextUrl;
                    try
                    {
                        response = await FetchWithHeaders(currentUrl, customHeaders);
                    }
                    catch (Exception ex)
                    {
                        throw new DownloadClientException("Failed to download resolved JSON URL for '{0}': {1}", remoteBook.Release.Title, ex.Message);
                    }

                    if (response.HasHttpError)
                    {
                        throw new DownloadClientException("Server returned status {0} for resolved JSON URL '{1}'", response.StatusCode, currentUrl);
                    }
                }
                else if (ct.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    var nextUrl = TryExtractHtmlDownloadLink(response, currentUrl);
                    if (string.IsNullOrWhiteSpace(nextUrl))
                    {
                        // HTML with no recognisable download link — log preview and fail clearly
                        var preview = GetContentPreview(response.ResponseData, 600);
                        throw new DownloadClientException(
                            "Expected a book file from '{0}' but got an HTML page with no download link. Preview: {1}",
                            currentUrl,
                            preview);
                    }

                    _logger.Debug("Resolved HTML download URL for '{0}': {1}", remoteBook.Release.Title, nextUrl);
                    currentUrl = nextUrl;
                    try
                    {
                        response = await FetchWithHeaders(currentUrl, customHeaders);
                    }
                    catch (Exception ex)
                    {
                        throw new DownloadClientException("Failed to download resolved HTML URL for '{0}': {1}", remoteBook.Release.Title, ex.Message);
                    }

                    if (response.HasHttpError)
                    {
                        throw new DownloadClientException("Server returned status {0} for resolved HTML URL '{1}'", response.StatusCode, currentUrl);
                    }
                }
                else
                {
                    break; // Binary content (EPUB, PDF, MOBI…) — stop resolving
                }
            }

            // Final sanity check: if we still have HTML here, something went wrong
            {
                var finalCt = response.Headers.ContentType ?? string.Empty;
                if (finalCt.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    var preview = GetContentPreview(response.ResponseData, 600);
                    throw new DownloadClientException(
                        "Download resolved to an HTML page instead of a book file from '{0}'. Preview: {1}",
                        currentUrl,
                        preview);
                }
            }

            // Determine filename
            var filename = GetFilename(response, remoteBook.Release.Title, currentUrl);

            // Ensure download folder exists
            _diskProvider.EnsureFolder(Settings.DownloadFolder);

            var filePath = Path.Combine(Settings.DownloadFolder, filename);
            using (var stream = _diskProvider.OpenWriteStream(filePath))
            {
                stream.Write(response.ResponseData, 0, response.ResponseData.Length);
            }

            _logger.Debug("HTTP download saved: {0}", filePath);

            // DownloadId must match the format used in GetItems() so Readarr can track the grab.
            return Definition.Name + "_" + Path.GetFileNameWithoutExtension(filename);
        }

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            // HTTP Blackhole downloads complete synchronously in Download(),
            // so all files in the download folder are already completed.
            if (!_diskProvider.FolderExists(Settings.DownloadFolder))
            {
                yield break;
            }

            foreach (var file in _diskProvider.GetFiles(Settings.DownloadFolder, false))
            {
                var title = Path.GetFileNameWithoutExtension(file);
                var info = _diskProvider.GetFileSize(file);

                yield return new DownloadClientItem
                {
                    DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, false),
                    DownloadId = Definition.Name + "_" + title,
                    Title = title,
                    TotalSize = info,
                    RemainingSize = 0,
                    OutputPath = new OsPath(file),
                    Status = DownloadItemStatus.Completed,
                    CanBeRemoved = true,
                    CanMoveFiles = true,
                };
            }
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            if (deleteData && item.OutputPath.IsValid)
            {
                _diskProvider.DeleteFile(item.OutputPath.FullPath);
            }
        }

        public override DownloadClientInfo GetStatus()
        {
            return new DownloadClientInfo
            {
                IsLocalhost = true,
                OutputRootFolders = new List<OsPath> { new OsPath(Settings.DownloadFolder) }
            };
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            failures.AddIfNotNull(TestFolder(Settings.DownloadFolder, "DownloadFolder"));
        }

        private async Task<HttpResponse> FetchWithHeaders(string url, Dictionary<string, string> extraHeaders)
        {
            var response = await ExecuteRequest(url, extraHeaders);

            if (IsDdosGuardChallenge(response))
            {
                _logger.Debug("DDoS-Guard challenge detected for {0}", url);

                string challengeCookie = null;

                // Prefer FlareSolverr when configured — it handles the full browser challenge
                if (!string.IsNullOrWhiteSpace(Settings.FlareSolverrUrl))
                {
                    challengeCookie = await SolveWithFlareSolverr(url);
                }

                // Fall back to built-in SHA1 PoW solver
                if (challengeCookie == null)
                {
                    _logger.Debug("Falling back to built-in DDoS-Guard PoW solver for {0}", url);
                    challengeCookie = SolveDdosGuardChallenge(response);
                }

                if (challengeCookie != null)
                {
                    var headersWithChallenge = new Dictionary<string, string>(extraHeaders ?? new Dictionary<string, string>());
                    var existing = headersWithChallenge.TryGetValue("Cookie", out var c) ? c + "; " : string.Empty;
                    headersWithChallenge["Cookie"] = existing + challengeCookie;
                    response = await ExecuteRequest(url, headersWithChallenge);
                    _logger.Debug("DDoS-Guard retry complete: {0}", response.StatusCode);
                }
                else
                {
                    _logger.Warn("DDoS-Guard challenge could not be solved for {0}", url);
                }
            }

            // Re-throw HTTP errors that are not DDoS-Guard challenges so callers see them normally.
            if (response.HasHttpError)
            {
                throw new HttpException(response);
            }

            return response;
        }

        private async Task<string> SolveWithFlareSolverr(string url)
        {
            try
            {
                var solveUrl = Settings.FlareSolverrUrl.TrimEnd('/') + "/v1";
                var body = $"{{\"cmd\":\"request.get\",\"url\":\"{url}\",\"maxTimeout\":60000}}";

                var request = new NzbDrone.Common.Http.HttpRequest(solveUrl);
                request.Method = HttpMethod.Post;
                request.SetContent(body);
                request.Headers.ContentType = "application/json";
                request.AllowAutoRedirect = true;
                request.SuppressHttpError = true;

                var fsResponse = await _httpClient.ExecuteAsync(request);

                if (fsResponse.HasHttpError)
                {
                    _logger.Warn("FlareSolverr returned HTTP {0} for {1}", fsResponse.StatusCode, url);
                    return null;
                }

                var json = JObject.Parse(fsResponse.Content);
                if (json["status"]?.ToString() != "ok")
                {
                    _logger.Warn("FlareSolverr error: {0}", json["message"]?.ToString() ?? "unknown");
                    return null;
                }

                var cookies = json["solution"]?["cookies"] as JArray;
                if (cookies == null || cookies.Count == 0)
                {
                    _logger.Warn("FlareSolverr solved challenge but returned no cookies for {0}", url);
                    return null;
                }

                var parts = new List<string>();
                foreach (var cookie in cookies)
                {
                    var name = cookie["name"]?.ToString();
                    var value = cookie["value"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        parts.Add($"{name}={value}");
                    }
                }

                var cookieStr = string.Join("; ", parts);
                _logger.Debug("FlareSolverr solved challenge for {0}, cookies: {1}", url, cookieStr);
                return cookieStr;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "FlareSolverr request failed for {0}", url);
                return null;
            }
        }

        private async Task<HttpResponse> ExecuteRequest(string url, Dictionary<string, string> extraHeaders)
        {
            var request = new HttpRequest(url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.AllowAutoRedirect = true;

            // Suppress HTTP errors so we can inspect the response body (e.g. DDoS-Guard challenge on 503).
            // Callers check response.HasHttpError themselves after DDoS-Guard handling.
            request.SuppressHttpError = true;

            if (extraHeaders != null)
            {
                foreach (var kvp in extraHeaders)
                {
                    request.Headers.Add(kvp.Key, kvp.Value);
                }
            }

            return await _httpClient.ExecuteAsync(request);
        }

        private static bool IsDdosGuardChallenge(HttpResponse response)
        {
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
            {
                return false;
            }

            var content = Encoding.UTF8.GetString(response.ResponseData);
            return content.Contains("c_token=", StringComparison.Ordinal)
                && content.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase);
        }

        // DDoS-Guard PoW: find i such that SHA1(c + i)[n1] == 0xB0 && SHA1(c + i)[n1+1] == 0x0B
        // where c is the 40-char hex token from the challenge script and n1 = int(c[0], 16).
        private static readonly Regex DdosGuardTokenRegex = new Regex(
            @"'([0-9A-Fa-f]{40})'",
            RegexOptions.Compiled);

        private string SolveDdosGuardChallenge(HttpResponse response)
        {
            var content = Encoding.UTF8.GetString(response.ResponseData);
            var match = DdosGuardTokenRegex.Match(content);
            if (!match.Success)
            {
                _logger.Warn("DDoS-Guard: could not extract PoW token from challenge page");
                return null;
            }

            var c = match.Groups[1].Value;
            var n1 = Convert.ToInt32(c[0].ToString(), 16); // first hex char as integer index

            _logger.Debug("DDoS-Guard PoW: token={0}, n1={1}", c, n1);

            using var sha1 = SHA1.Create();
            for (var i = 0; i < 10_000_000; i++)
            {
                var input = Encoding.UTF8.GetBytes(c + i);
                var hash = sha1.ComputeHash(input);
                if (hash.Length > n1 + 1 && hash[n1] == 0xB0 && hash[n1 + 1] == 0x0B)
                {
                    var token = c + i;
                    _logger.Debug("DDoS-Guard PoW solved: i={0}, token={1}", i, token);
                    return $"c_token={token}; c_time=0.1";
                }
            }

            _logger.Warn("DDoS-Guard PoW: could not find solution within iteration limit");
            return null;
        }

        // Anna's Archive: href="https://...">📚
        private static readonly Regex HtmlDownloadLinkRegex = new Regex(
            @"href=""(https?://[^""]+)""[^>]*>\s*📚",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Z-Library book page: <a class="addDownloadedBook ..." href="/dl/...">
        private static readonly Regex ZLibDlLinkRegex = new Regex(
            @"<a\s[^>]*href=""(/dl/[^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static string TryExtractHtmlDownloadLink(HttpResponse response, string requestUrl = null)
        {
            try
            {
                var content = Encoding.UTF8.GetString(response.ResponseData);

                // Anna's Archive — absolute URL with 📚 emoji
                var match = HtmlDownloadLinkRegex.Match(content);
                if (match.Success)
                {
                    return WebUtility.HtmlDecode(match.Groups[1].Value);
                }

                // Z-Library book page — relative /dl/ path
                var zlMatch = ZLibDlLinkRegex.Match(content);
                if (zlMatch.Success)
                {
                    var path = WebUtility.HtmlDecode(zlMatch.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(requestUrl))
                    {
                        // Resolve relative URL using the original request URL's origin
                        var uri = new Uri(requestUrl);
                        return $"{uri.Scheme}://{uri.Host}{path}";
                    }

                    return path;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string TryExtractDownloadUrlFromJson(HttpResponse response, string sourceUrl = null)
        {
            try
            {
                var content = Encoding.UTF8.GetString(response.ResponseData);
                var json = JToken.Parse(content);

                // Try multiple known response shapes (Z-Library, etc.)
                var url = json["download_url"]?.ToString()
                    ?? json["book"]?["download_url"]?.ToString()
                    ?? json["book"]?["download_link"]?.ToString()
                    ?? json["download_link"]?.ToString()
                    ?? json["file"]?["url"]?.ToString()
                    ?? json["href"]?.ToString();

                if (!string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }

                // Z-Library: book.dl is a relative path like "/dl/B97NmR8L58"
                var dlPath = json["book"]?["dl"]?.ToString() ?? json["dl"]?.ToString();
                if (string.IsNullOrWhiteSpace(dlPath))
                {
                    return null;
                }

                if (dlPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return dlPath;
                }

                // Resolve relative path: prefer book.href origin, fall back to source URL origin
                var baseHref = json["book"]?["href"]?.ToString();
                if (!string.IsNullOrWhiteSpace(baseHref))
                {
                    var baseUri = new Uri(baseHref);
                    return $"{baseUri.Scheme}://{baseUri.Host}{dlPath}";
                }

                if (!string.IsNullOrWhiteSpace(sourceUrl))
                {
                    var baseUri = new Uri(sourceUrl);
                    return $"{baseUri.Scheme}://{baseUri.Host}{dlPath}";
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetFilename(HttpResponse response, string releaseTitle, string sourceUrl = null)
        {
            // 1. Try Content-Disposition header.
            // RFC 6266: filename*= (RFC 5987 encoded) takes precedence over plain filename=
            var contentDisposition = response.Headers.GetValues("Content-Disposition");
            if (contentDisposition != null)
            {
                foreach (var value in contentDisposition)
                {
                    var fn = ParseContentDispositionFilename(value);
                    if (!fn.IsNullOrWhiteSpace())
                    {
                        return SanitizeFilename(fn);
                    }
                }
            }

            var safeTitle = SanitizeFilename(releaseTitle);

            // 2. Try Content-Type header
            var ext = GetExtensionFromContentType(response.Headers.ContentType);
            if (ext != ".bin")
            {
                return $"{safeTitle}{ext}";
            }

            // 3. Try extension from source URL path
            if (!string.IsNullOrWhiteSpace(sourceUrl))
            {
                try
                {
                    var urlPath = new Uri(sourceUrl).AbsolutePath;
                    var urlExt = Path.GetExtension(urlPath).ToLowerInvariant();
                    if (urlExt is ".epub" or ".pdf" or ".mobi" or ".azw3" or ".fb2")
                    {
                        return $"{safeTitle}{urlExt}";
                    }
                }
                catch
                {
                    // ignore malformed URLs
                }
            }

            // 4. Detect by magic bytes
            var magicExt = GetExtensionFromMagicBytes(response.ResponseData);
            return $"{safeTitle}{magicExt}";
        }

        // Parse Content-Disposition, preferring filename*= (RFC 5987) over plain filename=
        private static string ParseContentDispositionFilename(string header)
        {
            string simpleFilename = null;

            foreach (var part in header.Split(';'))
            {
                var trimmed = part.Trim();

                if (trimmed.StartsWith("filename*=", StringComparison.OrdinalIgnoreCase))
                {
                    var decoded = DecodeRfc5987Filename(trimmed.Substring("filename*=".Length).Trim());
                    if (!string.IsNullOrWhiteSpace(decoded))
                    {
                        return decoded; // highest priority — return immediately
                    }
                }
                else if (simpleFilename == null &&
                         trimmed.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
                {
                    simpleFilename = trimmed.Substring("filename=".Length).Trim('"', '\'', ' ');
                }
            }

            return simpleFilename;
        }

        // Decode RFC 5987 extended value: charset'language'percent-encoded-value
        private static string DecodeRfc5987Filename(string encoded)
        {
            // Split on first two single-quotes to get charset, language, value
            var first = encoded.IndexOf('\'');
            if (first < 0) return null;
            var second = encoded.IndexOf('\'', first + 1);
            if (second < 0) return null;

            var encodedValue = encoded.Substring(second + 1);
            if (string.IsNullOrWhiteSpace(encodedValue)) return null;

            try
            {
                // Uri.UnescapeDataString treats %XX as UTF-8 code units — correct for charset=UTF-8
                return Uri.UnescapeDataString(encodedValue);
            }
            catch
            {
                return null;
            }
        }

        private static string GetContentPreview(byte[] data, int maxChars)
        {
            if (data == null || data.Length == 0)
            {
                return "(empty response)";
            }

            try
            {
                var text = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, maxChars * 2));
                return text.Length > maxChars ? text.Substring(0, maxChars) + "..." : text;
            }
            catch
            {
                return $"(binary, {data.Length} bytes)";
            }
        }

        private static string GetExtensionFromContentType(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return ".bin";
            }

            return contentType.ToLowerInvariant() switch
            {
                var ct when ct.Contains("epub") => ".epub",
                var ct when ct.Contains("pdf") => ".pdf",
                var ct when ct.Contains("mobi") => ".mobi",
                _ => ".bin",
            };
        }

        private static string GetExtensionFromMagicBytes(byte[] data)
        {
            if (data == null || data.Length < 4)
            {
                return ".bin";
            }

            // PDF: %PDF
            if (data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46)
            {
                return ".pdf";
            }

            // ZIP / EPUB: PK\x03\x04
            if (data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04)
            {
                return ".epub";
            }

            // MOBI / PalmDOC: check bytes 60-63 for "BOOK" and 64-67 for "MOBI"
            if (data.Length >= 68 &&
                data[60] == 0x42 && data[61] == 0x4F && data[62] == 0x4F && data[63] == 0x4B &&
                data[64] == 0x4D && data[65] == 0x4F && data[66] == 0x42 && data[67] == 0x49)
            {
                return ".mobi";
            }

            return ".bin";
        }

        private static string SanitizeFilename(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Length > 200 ? name.Substring(0, 200) : name;
        }
    }
}

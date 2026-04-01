using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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

            // Some indexer APIs / pages return something other than the file itself.
            // Handle two known cases:
            //   1. HTML page (Anna's Archive slow_download) — contains a "📚 Download now" link
            //   2. JSON response (Z-Library EAPI) — contains a download_url field
            var ct = response.Headers.ContentType ?? string.Empty;

            if (ct.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedUrl = TryExtractHtmlDownloadLink(response, url);
                if (!string.IsNullOrWhiteSpace(resolvedUrl))
                {
                    _logger.Debug("Resolved HTML download URL for '{0}': {1}", remoteBook.Release.Title, resolvedUrl);
                    try
                    {
                        response = await FetchWithHeaders(resolvedUrl, customHeaders);
                    }
                    catch (Exception ex)
                    {
                        throw new DownloadClientException("Failed to download resolved HTML URL for '{0}': {1}", remoteBook.Release.Title, ex.Message);
                    }

                    if (response.HasHttpError)
                    {
                        throw new DownloadClientException("Server returned status {0} for resolved HTML URL '{1}'", response.StatusCode, resolvedUrl);
                    }
                }
            }

            if (ct.Contains("json", StringComparison.OrdinalIgnoreCase) || ct.Contains("javascript", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedUrl = TryExtractDownloadUrlFromJson(response);
                if (!string.IsNullOrWhiteSpace(resolvedUrl))
                {
                    _logger.Debug("Resolved JSON download URL for '{0}': {1}", remoteBook.Release.Title, resolvedUrl);
                    try
                    {
                        response = await FetchWithHeaders(resolvedUrl, customHeaders);
                    }
                    catch (Exception ex)
                    {
                        throw new DownloadClientException("Failed to download resolved URL for '{0}': {1}", remoteBook.Release.Title, ex.Message);
                    }

                    if (response.HasHttpError)
                    {
                        throw new DownloadClientException("Server returned status {0} for resolved URL '{1}'", response.StatusCode, resolvedUrl);
                    }
                }
            }

            // Determine filename
            var filename = GetFilename(response, remoteBook.Release.Title);

            // Ensure download folder exists
            _diskProvider.EnsureFolder(Settings.DownloadFolder);

            var filePath = Path.Combine(Settings.DownloadFolder, filename);
            using (var stream = _diskProvider.OpenWriteStream(filePath))
            {
                stream.Write(response.ResponseData, 0, response.ResponseData.Length);
            }

            _logger.Debug("HTTP download saved: {0}", filePath);

            return Path.GetFileNameWithoutExtension(filename);
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
            var request = new HttpRequest(url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Readarr/1.0)");
            request.AllowAutoRedirect = true;

            if (extraHeaders != null)
            {
                foreach (var kvp in extraHeaders)
                {
                    request.Headers.Add(kvp.Key, kvp.Value);
                }
            }

            return await _httpClient.ExecuteAsync(request);
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

        private static string TryExtractDownloadUrlFromJson(HttpResponse response)
        {
            try
            {
                var content = Encoding.UTF8.GetString(response.ResponseData);
                var json = JToken.Parse(content);

                // Try multiple known response shapes (Z-Library, etc.)
                return json["download_url"]?.ToString()
                    ?? json["book"]?["download_url"]?.ToString()
                    ?? json["book"]?["download_link"]?.ToString()
                    ?? json["download_link"]?.ToString()
                    ?? json["file"]?["url"]?.ToString()
                    ?? json["href"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string GetFilename(HttpResponse response, string releaseTitle)
        {
            // Try Content-Disposition header first
            var contentDisposition = response.Headers.GetValues("Content-Disposition");
            if (contentDisposition != null)
            {
                foreach (var value in contentDisposition)
                {
                    var fnIndex = value.IndexOf("filename=", StringComparison.OrdinalIgnoreCase);
                    if (fnIndex >= 0)
                    {
                        var fn = value.Substring(fnIndex + 9).Trim('"', ' ', ';');
                        if (!fn.IsNullOrWhiteSpace())
                        {
                            return SanitizeFilename(fn);
                        }
                    }
                }
            }

            // Fall back to content type → extension
            var ext = GetExtensionFromContentType(response.Headers.ContentType);
            var safeTitle = SanitizeFilename(releaseTitle);
            return $"{safeTitle}{ext}";
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
                var ct when ct.Contains("octet-stream") => ".bin",
                _ => ".bin",
            };
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

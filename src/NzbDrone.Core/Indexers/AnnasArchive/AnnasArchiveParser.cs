using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.AnnasArchive
{
    public class AnnasArchiveParser : IParseIndexerResponse
    {
        private readonly AnnasArchiveSettings _settings;
        private readonly IHttpClient _httpClient;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Matches each search result block: <div class="flex pt-3 pb-3 border-b ...">
        private static readonly Regex ResultBlockRegex = new Regex(
            @"<div[^>]+class=""[^""]*\bflex\b[^""]*\bpt-3\b[^""]*\bpb-3\b[^""]*\bborder-b\b[^""]*""[^>]*>(.*?)(?=<div[^>]+class=""[^""]*\bflex\b[^""]*\bpt-3\b[^""]*\bpb-3\b[^""]*\bborder-b\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        // MD5 hash from href="/md5/HASH"
        private static readonly Regex Md5LinkRegex = new Regex(
            @"href=""(/md5/([a-f0-9]{32})(?:[^""]*))""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Title from <a class="js-vim-focus ...">TITLE</a>
        private static readonly Regex TitleRegex = new Regex(
            @"<a[^>]+class=""[^""]*\bjs-vim-focus\b[^""]*""[^>]*>\s*(.*?)\s*</a>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        // Metadata div: <div class="... text-gray-800 font-semibold text-sm ...">FORMAT · SIZE · [LANG]</div>
        private static readonly Regex MetaDivRegex = new Regex(
            @"<div[^>]+class=""[^""]*\btext-gray-800\b[^""]*\bfont-semibold\b[^""]*\btext-sm\b[^""]*""[^>]*>(.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        // Language in brackets: [en] or [es]
        private static readonly Regex LangRegex = new Regex(
            @"\[([a-z]{2,5})\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // File size: 1.2 MB, 500 KB, etc.
        private static readonly Regex SizeRegex = new Regex(
            @"([\d.,]+\s*(?:B|KB|MB|GB|KiB|MiB|GiB))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Known file formats
        private static readonly Regex FormatRegex = new Regex(
            @"\b(epub|pdf|mobi|azw3|djvu|fb2|doc|rtf|cbz|cbr)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public AnnasArchiveParser(AnnasArchiveSettings settings, IHttpClient httpClient = null)
        {
            _settings = settings;
            _httpClient = httpClient;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            if (indexerResponse.HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new IndexerException(indexerResponse,
                    "Unexpected response status {0} from Anna's Archive",
                    indexerResponse.HttpResponse.StatusCode);
            }

            var content = indexerResponse.Content;

            // Try JSON parsing first
            if (content.TrimStart().StartsWith("[") || content.TrimStart().StartsWith("{"))
            {
                try
                {
                    return ParseJson(content);
                }
                catch (JsonException)
                {
                    // Fall through to HTML parsing
                }
            }

            // Fallback: HTML parsing
            return ParseHtml(content);
        }

        private IList<ReleaseInfo> ParseJson(string content)
        {
            var results = new List<ReleaseInfo>();
            var token = JToken.Parse(content);

            JArray books;
            if (token is JArray arr)
            {
                books = arr;
            }
            else if (token is JObject obj && obj["results"] is JArray resultsArr)
            {
                books = resultsArr;
            }
            else if (token is JObject obj2 && obj2["books"] is JArray booksArr)
            {
                books = booksArr;
            }
            else
            {
                return results;
            }

            foreach (var item in books)
            {
                try
                {
                    var book = item.ToObject<AnnasArchiveBook>();
                    if (book?.Md5.IsNullOrWhiteSpace() != false)
                    {
                        continue;
                    }

                    results.Add(BuildRelease(book));
                }
                catch
                {
                    // Skip malformed entries
                }
            }

            return results;
        }

        private IList<ReleaseInfo> ParseHtml(string html)
        {
            var results = new List<ReleaseInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Try block-based parsing first (uses correct CSS selectors from Anna's Archive HTML)
            var blocks = ResultBlockRegex.Matches(html);
            if (blocks.Count > 0)
            {
                foreach (Match block in blocks)
                {
                    var blockHtml = block.Groups[1].Value;
                    var md5Match = Md5LinkRegex.Match(blockHtml);
                    if (!md5Match.Success)
                    {
                        continue;
                    }

                    var md5 = md5Match.Groups[2].Value;
                    if (!seen.Add(md5))
                    {
                        continue;
                    }

                    var title = string.Empty;
                    var titleMatch = TitleRegex.Match(blockHtml);
                    if (titleMatch.Success)
                    {
                        title = WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups[1].Value, @"<[^>]+>", " ").Trim());
                    }

                    var ext = string.Empty;
                    var lang = string.Empty;
                    long size = 0;

                    var metaMatch = MetaDivRegex.Match(blockHtml);
                    if (metaMatch.Success)
                    {
                        var metaText = WebUtility.HtmlDecode(Regex.Replace(metaMatch.Groups[1].Value, @"<[^>]+>", " "));
                        var segments = metaText.Split('·');
                        foreach (var seg in segments)
                        {
                            var s = seg.Trim();
                            if (string.IsNullOrWhiteSpace(s))
                            {
                                continue;
                            }

                            var fmtMatch = FormatRegex.Match(s);
                            if (fmtMatch.Success && string.IsNullOrEmpty(ext))
                            {
                                ext = fmtMatch.Value.ToLowerInvariant();
                                continue;
                            }

                            var sizeMatch = SizeRegex.Match(s);
                            if (sizeMatch.Success && size == 0)
                            {
                                size = ParseSize(sizeMatch.Value);
                                continue;
                            }

                            var langMatch = LangRegex.Match(s);
                            if (langMatch.Success && string.IsNullOrEmpty(lang))
                            {
                                lang = langMatch.Groups[1].Value;
                            }
                        }
                    }

                    results.Add(new ReleaseInfo
                    {
                        Guid = $"AnnasArchive-{md5}",
                        Title = BuildTitle(title, string.Empty, 0, ext, lang),
                        Book = title,
                        DownloadUrl = BuildDownloadUrl(md5),
                        InfoUrl = $"{_settings.BaseUrl.TrimEnd('/')}/md5/{md5}",
                        Size = size,
                        DownloadProtocol = DownloadProtocol.Unknown,
                        PublishDate = DateTime.UtcNow,
                    });
                }

                if (results.Count > 0)
                {
                    Logger.Debug("Anna's Archive HTML parser found {0} results via block matching", results.Count);
                    return results;
                }
            }

            // Fallback: find any /md5/ links anywhere in the page
            Logger.Debug("Anna's Archive block regex found no results, falling back to simple MD5 scan");
            var allMd5 = Md5LinkRegex.Matches(html);
            foreach (Match match in allMd5)
            {
                var md5 = match.Groups[2].Value;
                if (!seen.Add(md5))
                {
                    continue;
                }

                results.Add(new ReleaseInfo
                {
                    Guid = $"AnnasArchive-{md5}",
                    Title = $"Unknown - {md5}",
                    DownloadUrl = BuildDownloadUrl(md5),
                    InfoUrl = $"{_settings.BaseUrl.TrimEnd('/')}/md5/{md5}",
                    DownloadProtocol = DownloadProtocol.Unknown,
                    PublishDate = DateTime.UtcNow,
                });
            }

            Logger.Debug("Anna's Archive HTML fallback found {0} MD5 links", results.Count);
            return results;
        }

        private ReleaseInfo BuildRelease(AnnasArchiveBook book)
        {
            int.TryParse(book.Year, out var year);

            return new ReleaseInfo
            {
                Guid = $"AnnasArchive-{book.Md5}",
                Title = BuildTitle(book.Title, book.Author, year, book.Extension, book.Language),
                Author = WebUtility.HtmlDecode(book.Author ?? string.Empty).Trim(),
                Book = WebUtility.HtmlDecode(book.Title ?? string.Empty).Trim(),
                DownloadUrl = BuildDownloadUrl(book.Md5),
                InfoUrl = $"{_settings.BaseUrl.TrimEnd('/')}/md5/{book.Md5}",
                Size = book.Filesize,
                DownloadProtocol = DownloadProtocol.Unknown,
                PublishDate = year > 0 ? new DateTime(year, 1, 1) : DateTime.UtcNow,
            };
        }

        private string BuildTitle(string title, string author, int year, string ext, string lang)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(author))
            {
                parts.Add(WebUtility.HtmlDecode(author).Trim());
            }

            parts.Add("-");
            parts.Add(string.IsNullOrWhiteSpace(title) ? "Unknown Title" : WebUtility.HtmlDecode(title).Trim());

            var tags = new List<string>();
            if (year > 0)
            {
                tags.Add(year.ToString());
            }

            if (!string.IsNullOrWhiteSpace(ext))
            {
                tags.Add(ext.ToUpperInvariant());
            }

            if (!string.IsNullOrWhiteSpace(lang))
            {
                tags.Add(lang.ToUpperInvariant());
            }

            if (tags.Count > 0)
            {
                parts.Add($"[{string.Join(", ", tags)}]");
            }

            return string.Join(" ", parts);
        }

        private string BuildDownloadUrl(string md5)
        {
            if (!string.IsNullOrWhiteSpace(_settings.ApiKey) && _httpClient != null)
            {
                return ResolveFastDownloadUrl(md5);
            }

            // Slow download server #1 — HttpBlackhole will parse the HTML page,
            // extract the "📚 Download now" link, and follow it to the actual file.
            return $"{_settings.BaseUrl.TrimEnd('/')}/slow_download/{md5}/0/0";
        }

        private string ResolveFastDownloadUrl(string md5)
        {
            var apiUrl = $"{_settings.BaseUrl.TrimEnd('/')}/dyn/api/fast_download.json?md5={md5}";
            try
            {
                var request = new HttpRequest(apiUrl);
                request.Headers.Add("Authorization", $"Bearer {_settings.ApiKey}");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Readarr/1.0)");

                var response = _httpClient.ExecuteAsync(request).GetAwaiter().GetResult();
                if (!response.HasHttpError && !string.IsNullOrWhiteSpace(response.Content))
                {
                    var json = JObject.Parse(response.Content);
                    var downloadUrl = json["download_url"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        return downloadUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to resolve Anna's Archive fast download URL for md5 {0}", md5);
            }

            return $"{_settings.BaseUrl.TrimEnd('/')}/md5/{md5}";
        }


        private static readonly Regex SizeParseRegex = new Regex(
            @"^([\d.,]+)\s*(B|KB|MB|GB|KiB|MiB|GiB)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static long ParseSize(string sizeStr)
        {
            if (string.IsNullOrWhiteSpace(sizeStr))
            {
                return 0;
            }

            var m = SizeParseRegex.Match(sizeStr.Trim());
            if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(",", "."), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return 0;
            }

            return m.Groups[2].Value.ToUpperInvariant() switch
            {
                "KB" or "KIB" => (long)(value * 1024),
                "MB" or "MIB" => (long)(value * 1024 * 1024),
                "GB" or "GIB" => (long)(value * 1024 * 1024 * 1024),
                _ => (long)value,
            };
        }
    }
}

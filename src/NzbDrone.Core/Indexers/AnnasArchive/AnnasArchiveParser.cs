using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.AnnasArchive
{
    public class AnnasArchiveParser : IParseIndexerResponse
    {
        private readonly AnnasArchiveSettings _settings;

        // Fallback HTML parser: extracts /md5/HASH links and surrounding metadata
        private static readonly Regex Md5LinkRegex = new Regex(
            @"href=""/md5/([a-f0-9]{32})""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Metadata pattern: "Author, Publisher, Year, lang, ext, size"
        private static readonly Regex MetaRegex = new Regex(
            @"(?<year>\d{4})[,\s]+(?<lang>[a-z]{2,3})[,\s]+(?<ext>epub|pdf|mobi|azw3|djvu|fb2|doc|rtf)[,\s]+(?<size>[\d.,]+ (?:KB|MB|kB|Mb|Gb|B))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public AnnasArchiveParser(AnnasArchiveSettings settings)
        {
            _settings = settings;
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
            var matches = Md5LinkRegex.Matches(html);

            foreach (Match match in matches)
            {
                var md5 = match.Groups[1].Value;
                var surroundingText = ExtractSurroundingText(html, match.Index, 500);
                var (title, author, year, lang, ext, size) = ParseSurroundingText(surroundingText);

                var release = new ReleaseInfo
                {
                    Guid = $"AnnasArchive-{md5}",
                    Title = BuildTitle(title, author, year, ext, lang),
                    Author = author,
                    Book = title,
                    DownloadUrl = BuildDownloadUrl(md5),
                    InfoUrl = $"{_settings.BaseUrl.TrimEnd('/')}/md5/{md5}",
                    Size = size,
                    DownloadProtocol = DownloadProtocol.Unknown,
                    PublishDate = year > 0 ? new DateTime(year, 1, 1) : DateTime.UtcNow,
                };

                results.Add(release);
            }

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
            // Use Anna's Archive fast download endpoint
            return $"{_settings.BaseUrl.TrimEnd('/')}/md5/{md5}";
        }

        private static string ExtractSurroundingText(string html, int position, int length)
        {
            var start = Math.Max(0, position - 50);
            var end = Math.Min(html.Length, position + length);
            return html.Substring(start, end - start);
        }

        private static (string title, string author, int year, string lang, string ext, long size) ParseSurroundingText(string text)
        {
            // Strip HTML tags
            var clean = Regex.Replace(text, @"<[^>]+>", " ");
            clean = WebUtility.HtmlDecode(clean);
            clean = Regex.Replace(clean, @"\s+", " ").Trim();

            var metaMatch = MetaRegex.Match(clean);

            var year = 0;
            var lang = string.Empty;
            var ext = string.Empty;
            long size = 0;

            if (metaMatch.Success)
            {
                int.TryParse(metaMatch.Groups["year"].Value, out year);
                lang = metaMatch.Groups["lang"].Value;
                ext = metaMatch.Groups["ext"].Value;
                size = ParseSize(metaMatch.Groups["size"].Value);
            }

            return (string.Empty, string.Empty, year, lang, ext, size);
        }

        private static long ParseSize(string sizeStr)
        {
            if (string.IsNullOrWhiteSpace(sizeStr))
            {
                return 0;
            }

            var parts = sizeStr.Trim().Split(' ');
            if (parts.Length < 2 || !double.TryParse(parts[0].Replace(",", "."), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return 0;
            }

            return parts[1].ToUpperInvariant() switch
            {
                "KB" or "KB" => (long)(value * 1024),
                "MB" or "MB" => (long)(value * 1024 * 1024),
                "GB" or "GB" => (long)(value * 1024 * 1024 * 1024),
                _ => (long)value,
            };
        }
    }
}

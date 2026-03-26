using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.ZLibrary
{
    public class ZLibraryParser : IParseIndexerResponse
    {
        private readonly ZLibrarySettings _settings;

        public ZLibraryParser(ZLibrarySettings settings)
        {
            _settings = settings;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            if (indexerResponse.HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new IndexerException(indexerResponse,
                    "Unexpected response status {0} from Z-Library",
                    indexerResponse.HttpResponse.StatusCode);
            }

            var content = indexerResponse.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return Array.Empty<ReleaseInfo>();
            }

            var results = new List<ReleaseInfo>();

            try
            {
                var token = JToken.Parse(content);
                JArray books = null;

                if (token is JArray arr)
                {
                    books = arr;
                }
                else if (token is JObject obj)
                {
                    books = (obj["books"] ?? obj["data"] ?? obj["results"]) as JArray;
                }

                if (books == null)
                {
                    return results;
                }

                foreach (var item in books)
                {
                    try
                    {
                        var book = item.ToObject<ZLibraryBook>();
                        if (book == null || book.Id.IsNullOrWhiteSpace())
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
            }
            catch (JsonException ex)
            {
                throw new IndexerException(indexerResponse, "Failed to parse Z-Library response: {0}", ex.Message);
            }

            return results;
        }

        private ReleaseInfo BuildRelease(ZLibraryBook book)
        {
            int.TryParse(book.Year, out var year);

            var author = WebUtility.HtmlDecode(book.GetAuthor()).Trim();
            var title = WebUtility.HtmlDecode(book.GetTitle() ?? string.Empty).Trim();
            var ext = book.Extension?.ToUpperInvariant() ?? string.Empty;
            var lang = book.Language ?? string.Empty;

            var releaseTitle = BuildTitle(title, author, year, ext, lang);

            return new ReleaseInfo
            {
                Guid = $"ZLibrary-{book.Id}",
                Title = releaseTitle,
                Author = author,
                Book = title,
                DownloadUrl = BuildDownloadUrl(book),
                InfoUrl = BuildInfoUrl(book),
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
                parts.Add(author);
            }

            parts.Add("-");
            parts.Add(string.IsNullOrWhiteSpace(title) ? "Unknown Title" : title);

            var tags = new List<string>();
            if (year > 0)
            {
                tags.Add(year.ToString());
            }

            if (!string.IsNullOrWhiteSpace(ext))
            {
                tags.Add(ext);
            }

            if (!string.IsNullOrWhiteSpace(lang))
            {
                tags.Add(lang);
            }

            if (tags.Count > 0)
            {
                parts.Add($"[{string.Join(", ", tags)}]");
            }

            return string.Join(" ", parts);
        }

        private string BuildDownloadUrl(ZLibraryBook book)
        {
            // Direct download URL: /{id}/{hash}/file/download
            // Falls back to book page if hash is missing
            var baseUrl = _settings.BaseUrl.TrimEnd('/');

            if (!string.IsNullOrWhiteSpace(book.Id) && !string.IsNullOrWhiteSpace(book.Hash))
            {
                return $"{baseUrl}/eapi/book/{book.Id}/{book.Hash}";
            }

            return BuildInfoUrl(book);
        }

        private string BuildInfoUrl(ZLibraryBook book)
        {
            var baseUrl = _settings.BaseUrl.TrimEnd('/');

            if (!string.IsNullOrWhiteSpace(book.Url))
            {
                return book.Url.StartsWith("http") ? book.Url : $"{baseUrl}{book.Url}";
            }

            if (!string.IsNullOrWhiteSpace(book.Id))
            {
                return $"{baseUrl}/book/{book.Id}";
            }

            return baseUrl;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.ZLibrary
{
    public class ZLibraryParser : IParseIndexerResponse
    {
        private const string TorOnionUrl = "http://bookszlibb74ugqojhzhg2a63w5i2atv5bqarulgczawnbmsb6s6qead.onion";

        private readonly ZLibrarySettings _settings;
        private readonly ICached<Dictionary<string, string>> _authCache;
        private readonly IConfigService _configService;

        private bool UseTor => _settings.UseTor || _configService?.TorProxyEnabled == true;
        private string CacheKey => UseTor ? TorOnionUrl : (_settings.BaseUrl?.TrimEnd('/') ?? "https://singlelogin.rs");

        public ZLibraryParser(ZLibrarySettings settings,
            ICached<Dictionary<string, string>> authCache = null,
            IConfigService configService = null)
        {
            _settings = settings;
            _authCache = authCache;
            _configService = configService;
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
                CustomDownloadHeaders = GetAuthHeaders(),
            };
        }

        private static string BuildTitle(string title, string author, int year, string ext, string lang)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(author))
            {
                parts.Add(author);
                parts.Add("-");
            }

            parts.Add(string.IsNullOrWhiteSpace(title) ? "Unknown Title" : title);

            var tags = new List<string>();
            if (year > 0) tags.Add(year.ToString());
            if (!string.IsNullOrWhiteSpace(ext)) tags.Add(ext);
            if (!string.IsNullOrWhiteSpace(lang)) tags.Add(lang);

            if (tags.Count > 0)
            {
                parts.Add($"[{string.Join(", ", tags)}]");
            }

            return string.Join(" ", parts);
        }

        private string BuildDownloadUrl(ZLibraryBook book)
        {
            var baseUrl = UseTor ? TorOnionUrl : (_settings.BaseUrl?.TrimEnd('/') ?? "https://singlelogin.rs");

            if (!string.IsNullOrWhiteSpace(book.DownloadUrl))
            {
                return book.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? book.DownloadUrl
                    : $"{baseUrl}{book.DownloadUrl}";
            }

            if (!string.IsNullOrWhiteSpace(book.DownloadLink))
            {
                return book.DownloadLink.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? book.DownloadLink
                    : $"{baseUrl}{book.DownloadLink}";
            }

            // EAPI download endpoint
            if (!string.IsNullOrWhiteSpace(book.Id) && !string.IsNullOrWhiteSpace(book.Hash))
            {
                return $"{baseUrl}/eapi/book/{book.Id}/{book.Hash}";
            }

            return BuildInfoUrl(book);
        }

        private string BuildInfoUrl(ZLibraryBook book)
        {
            var baseUrl = UseTor ? TorOnionUrl : (_settings.BaseUrl?.TrimEnd('/') ?? "https://singlelogin.rs");

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

        private Dictionary<string, string> GetAuthHeaders()
        {
            var session = _authCache?.Find(CacheKey);
            if (session == null) return null;

            if (session.TryGetValue("cookieString", out var cookieStr) && !string.IsNullOrWhiteSpace(cookieStr))
            {
                return new Dictionary<string, string> { { "Cookie", cookieStr } };
            }

            if (session.TryGetValue("userId", out var userId) &&
                session.TryGetValue("userKey", out var userKey) &&
                !string.IsNullOrWhiteSpace(userId) &&
                !string.IsNullOrWhiteSpace(userKey))
            {
                return new Dictionary<string, string>
                {
                    { "remix-userid", userId },
                    { "remix-userkey", userKey }
                };
            }

            return null;
        }
    }
}

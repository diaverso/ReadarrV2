using System;
using System.Linq;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.GoogleBooks.Resources;

namespace NzbDrone.Core.MetadataSource.GoogleBooks
{
    /// <summary>
    /// Provides book metadata enrichment from the Google Books API.
    /// This is NOT a primary metadata source — it is used exclusively to supplement
    /// data (ratings, covers, genres) returned by a primary source such as OpenLibraryProxy.
    ///
    /// No API key is required for basic usage (up to ~1000 requests/day per IP).
    /// The enrichment is best-effort: if Google Books returns nothing, the book is unchanged.
    /// </summary>
    public interface IGoogleBooksProxy
    {
        /// <summary>
        /// Enriches the given book in-place with ratings and cover data from Google Books.
        /// Only enriches if the book is missing ratings or cover images.
        /// </summary>
        void EnrichBook(Book book);
    }

    public class GoogleBooksProxy : IGoogleBooksProxy
    {
        private const string BaseUrl = "https://www.googleapis.com/books/v1/volumes";
        private const string UserAgent = "Readarr/1.0 (https://github.com/Readarr/Readarr)";

        private static readonly JsonSerializerOptions SerializerSettings = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly Logger _logger;

        public GoogleBooksProxy(ICachedHttpResponseService cachedHttpClient,
                                Logger logger)
        {
            _cachedHttpClient = cachedHttpClient;
            _logger = logger;
        }

        public void EnrichBook(Book book)
        {
            if (book == null)
            {
                return;
            }

            // Only enrich if we're missing ratings or cover
            var needsRatings = book.Ratings == null || book.Ratings.Votes == 0;
            var monitoredEdition = book.Editions?.Value?.FirstOrDefault(e => e.Monitored);
            var needsCover = monitoredEdition != null && !monitoredEdition.Images.Any();

            if (!needsRatings && !needsCover)
            {
                return;
            }

            _logger.Debug("GoogleBooks: Enriching book '{0}'", book.Title);

            var volume = FindVolume(
                monitoredEdition?.Isbn13,
                book.Title,
                book.AuthorMetadata?.Value?.Name);

            if (volume?.VolumeInfo == null)
            {
                return;
            }

            var info = volume.VolumeInfo;

            if (needsRatings && info.RatingsCount.GetValueOrDefault() > 0)
            {
                var ratings = new Ratings
                {
                    Votes = info.RatingsCount ?? 0,
                    Value = (decimal)(info.AverageRating ?? 0)
                };

                book.Ratings = ratings;

                if (monitoredEdition != null)
                {
                    monitoredEdition.Ratings = ratings;
                }

                _logger.Debug(
                    "GoogleBooks: Added ratings ({0} votes, {1} avg) for '{2}'",
                    ratings.Votes,
                    ratings.Value,
                    book.Title);
            }

            if (needsCover && monitoredEdition != null)
            {
                var coverUrl = info.ImageLinks?.Thumbnail ?? info.ImageLinks?.SmallThumbnail;

                if (coverUrl.IsNotNullOrWhiteSpace())
                {
                    // Google Books returns http:// URLs — upgrade to https
                    coverUrl = coverUrl.Replace("http://", "https://");

                    monitoredEdition.Images.Add(new MediaCover.MediaCover
                    {
                        Url = coverUrl,
                        CoverType = MediaCoverTypes.Cover
                    });

                    _logger.Debug("GoogleBooks: Added cover for '{0}'", book.Title);
                }
            }

            if (book.Genres == null || !book.Genres.Any())
            {
                if (info.Categories != null && info.Categories.Any())
                {
                    book.Genres = info.Categories.Take(5).ToList();
                }
            }

            if (monitoredEdition != null && monitoredEdition.Overview.IsNullOrWhiteSpace()
                && info.Description.IsNotNullOrWhiteSpace())
            {
                monitoredEdition.Overview = info.Description;
            }
        }

        private GoogleBooksVolume FindVolume(string isbn13, string title, string authorName)
        {
            // Try ISBN first — most reliable
            if (isbn13.IsNotNullOrWhiteSpace())
            {
                var byIsbn = SearchGoogle($"isbn:{isbn13}");
                if (byIsbn != null)
                {
                    return byIsbn;
                }
            }

            // Fall back to title + author
            if (title.IsNotNullOrWhiteSpace())
            {
                var query = authorName.IsNotNullOrWhiteSpace()
                    ? $"intitle:{Uri.EscapeDataString(title)}+inauthor:{Uri.EscapeDataString(authorName)}"
                    : $"intitle:{Uri.EscapeDataString(title)}";

                return SearchGoogle(query);
            }

            return null;
        }

        private GoogleBooksVolume SearchGoogle(string query)
        {
            try
            {
                var url = $"{BaseUrl}?q={query}&maxResults=1&printType=books";
                var httpRequest = new HttpRequest(url);
                httpRequest.Headers.Set("User-Agent", UserAgent);

                var httpResponse = _cachedHttpClient.Get(httpRequest, useCache: true, TimeSpan.FromDays(7));

                if (httpResponse.HasHttpError)
                {
                    _logger.Debug(
                        "GoogleBooks: Search returned {0} for query '{1}'",
                        httpResponse.StatusCode,
                        query);
                    return null;
                }

                var response = JsonSerializer.Deserialize<GoogleBooksSearchResponse>(
                    httpResponse.Content, SerializerSettings);

                return response?.Items?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "GoogleBooks: Error searching for '{0}'", query);
                return null;
            }
        }
    }
}

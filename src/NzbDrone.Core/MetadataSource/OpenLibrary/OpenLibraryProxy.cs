using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.OpenLibrary.Resources;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Provides book metadata from Open Library (https://openlibrary.org).
    /// Activated when IConfigService.MetadataSource == "openlibrary".
    ///
    /// NOTE: This class does NOT implement the IProvide* / ISearchFor* interfaces directly.
    /// Instead, it is injected into BookInfoProxy which delegates to it when the config
    /// MetadataSource is set to "openlibrary". This avoids DryIoc multiple-registration
    /// ambiguity since BookInfoProxy is the single registered implementation of those interfaces.
    /// </summary>
    public class OpenLibraryProxy
    {
        private const string BaseUrl = "https://openlibrary.org";
        private const string CoversBaseUrl = "https://covers.openlibrary.org";
        private const string UserAgent = "Readarr/1.0 (https://github.com/Readarr/Readarr)";
        private const int MaxEditionsPerWork = 3;
        private const int MaxConcurrentRequests = 5;

        private static readonly JsonSerializerOptions SerializerSettings = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IHttpClient _httpClient;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly Logger _logger;

        public OpenLibraryProxy(IHttpClient httpClient,
                                IAuthorService authorService,
                                IBookService bookService,
                                IEditionService editionService,
                                Logger logger)
        {
            _httpClient = httpClient;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _logger = logger;
        }

        /// <summary>
        /// Open Library's recentchanges API is not equivalent to "authors changed since date".
        /// Returning null signals RefreshAuthorService to skip incremental refresh.
        /// </summary>
        public HashSet<string> GetChangedAuthors(DateTime startTime) => null;

        public Author GetAuthorInfo(string foreignAuthorId, bool useCache = true)
        {
            _logger.Debug("OpenLibrary: Getting author info for {0}", foreignAuthorId);

            var authorResource = FetchAuthor(foreignAuthorId);
            var worksResponse = FetchAuthorWorks(foreignAuthorId);

            var metadata = MapAuthorMetadata(authorResource);
            var books = MapAuthorWorksToBooks(worksResponse, metadata);

            var result = new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = books,
                Series = new List<Series>()
            };

            foreach (var book in books)
            {
                book.AuthorMetadata = metadata;
                AddDbIds(foreignAuthorId, book, new Dictionary<string, AuthorMetadata> { { foreignAuthorId, metadata } });
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // IProvideBookInfo
        // ─────────────────────────────────────────────────────────────────────
        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignBookId)
        {
            _logger.Debug("OpenLibrary: Getting book info for {0}", foreignBookId);

            var workResource = FetchWork(foreignBookId);

            if (workResource.Authors == null || !workResource.Authors.Any())
            {
                throw new BookNotFoundException(foreignBookId);
            }

            var primaryAuthorOlId = workResource.Authors[0].Author?.OlId;

            OpenLibraryAuthorResource authorResource = null;
            AuthorMetadata authorMetadata;

            if (primaryAuthorOlId.IsNotNullOrWhiteSpace())
            {
                try
                {
                    authorResource = FetchAuthor(primaryAuthorOlId);
                    authorMetadata = MapAuthorMetadata(authorResource);
                }
                catch (AuthorNotFoundException)
                {
                    _logger.Warn("OpenLibrary: Author {0} not found for work {1}", primaryAuthorOlId, foreignBookId);
                    authorMetadata = new AuthorMetadata { ForeignAuthorId = primaryAuthorOlId ?? foreignBookId, Name = "Unknown Author" };
                }
            }
            else
            {
                authorMetadata = new AuthorMetadata { ForeignAuthorId = foreignBookId, Name = "Unknown Author" };
                primaryAuthorOlId = foreignBookId;
            }

            var editionsResponse = FetchEditions(foreignBookId);
            var book = MapBook(workResource, editionsResponse);
            book.AuthorMetadata = authorMetadata;

            AddDbIds(primaryAuthorOlId, book, new Dictionary<string, AuthorMetadata> { { primaryAuthorOlId, authorMetadata } });

            return Tuple.Create(primaryAuthorOlId, book, new List<AuthorMetadata> { authorMetadata });
        }

        // ─────────────────────────────────────────────────────────────────────
        // ISearchForNewBook
        // ─────────────────────────────────────────────────────────────────────
        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            _logger.Debug("OpenLibrary: Searching for book '{0}' by '{1}'", title, author);

            // Handle special prefixes (same as BookInfoProxy)
            var lowerTitle = title.ToLowerInvariant();
            var split = lowerTitle.Split(':');
            var prefix = split[0];

            if (split.Length == 2 && new[] { "author", "work", "edition", "isbn", "asin" }.Contains(prefix))
            {
                var slug = split[1].Trim();

                if (slug.IsNullOrWhiteSpace())
                {
                    return new List<Book>();
                }

                if (prefix == "work")
                {
                    try
                    {
                        var tuple = GetBookInfo(slug);
                        return tuple != null ? new List<Book> { tuple.Item2 } : new List<Book>();
                    }
                    catch
                    {
                        return new List<Book>();
                    }
                }

                if (prefix == "author")
                {
                    try
                    {
                        var authorResult = GetAuthorInfo(slug);
                        return authorResult?.Books?.Value ?? new List<Book>();
                    }
                    catch
                    {
                        return new List<Book>();
                    }
                }

                if (prefix == "edition")
                {
                    return SearchByForeignEditionId(slug, getAllEditions);
                }

                if (prefix == "isbn")
                {
                    return SearchByIsbn(slug);
                }

                if (prefix == "asin")
                {
                    return SearchByAsin(slug);
                }
            }

            // General search
            var query = author.IsNotNullOrWhiteSpace() ? $"{title} {author}" : title;
            return SearchOpenLibrary(query, getAllEditions);
        }

        public List<Book> SearchByIsbn(string isbn)
        {
            _logger.Debug("OpenLibrary: Searching by ISBN {0}", isbn);

            try
            {
                // GET /isbn/{isbn}.json redirects to /books/{OL_ID}.json
                var httpRequest = BuildRequest($"/isbn/{isbn}.json");
                httpRequest.AllowAutoRedirect = false;
                httpRequest.SuppressHttpError = true;

                var httpResponse = _httpClient.Get(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    return new List<Book>();
                }

                string editionKey;

                if (httpResponse.HasHttpRedirect)
                {
                    var location = httpResponse.Headers.GetSingleValue("Location");
                    editionKey = location?.Split('/')[^1];
                }
                else if (httpResponse.StatusCode == HttpStatusCode.OK)
                {
                    var edition = JsonSerializer.Deserialize<OpenLibraryEditionResource>(httpResponse.Content, SerializerSettings);
                    editionKey = edition?.OlId;
                }
                else
                {
                    return new List<Book>();
                }

                if (editionKey.IsNullOrWhiteSpace())
                {
                    return new List<Book>();
                }

                return SearchByForeignEditionId(editionKey, true);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "OpenLibrary: Error searching by ISBN {0}", isbn);
                return new List<Book>();
            }
        }

        public List<Book> SearchByAsin(string asin)
        {
            // Open Library does not index ASINs; fall back to empty
            return new List<Book>();
        }

        public List<Book> SearchByForeignEditionId(string foreignEditionId, bool getAllEditions)
        {
            _logger.Debug("OpenLibrary: Searching by edition ID {0}", foreignEditionId);

            try
            {
                var editionResource = FetchEdition(foreignEditionId);

                if (editionResource?.Works == null || !editionResource.Works.Any())
                {
                    return new List<Book>();
                }

                var workOlId = editionResource.Works[0].OlId;
                var tuple = GetBookInfo(workOlId);

                if (tuple == null)
                {
                    return new List<Book>();
                }

                var book = tuple.Item2;

                if (!getAllEditions)
                {
                    // Trim to just the requested edition
                    var matchedEdition = book.Editions.Value.SingleOrDefault(e => e.ForeignEditionId == foreignEditionId);
                    if (matchedEdition != null)
                    {
                        matchedEdition.Monitored = true;
                        book.Editions = new List<Edition> { matchedEdition };
                    }
                }

                return new List<Book> { book };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "OpenLibrary: Error searching by edition ID {0}", foreignEditionId);
                return new List<Book>();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ISearchForNewAuthor
        // ─────────────────────────────────────────────────────────────────────
        public List<Author> SearchForNewAuthor(string authorName)
        {
            if (authorName.IsNullOrWhiteSpace())
            {
                return new List<Author>();
            }

            _logger.Debug("OpenLibrary: Searching for author '{0}'", authorName);

            try
            {
                var url = $"{BaseUrl}/search/authors.json?q={Uri.EscapeDataString(authorName)}&limit=10";
                var httpRequest = BuildRequest(null, url);
                httpRequest.SuppressHttpError = true;

                var httpResponse = _httpClient.Get(httpRequest);

                if (httpResponse.HasHttpError)
                {
                    _logger.Warn("OpenLibrary: Author search failed with status {0}", httpResponse.StatusCode);
                    return new List<Author>();
                }

                var response = JsonSerializer.Deserialize<OpenLibraryAuthorSearchResponse>(httpResponse.Content, SerializerSettings);

                if (response?.Docs == null || !response.Docs.Any())
                {
                    return new List<Author>();
                }

                var authors = new List<Author>();

                foreach (var doc in response.Docs.Take(5))
                {
                    try
                    {
                        var author = GetAuthorInfo(doc.OlId, useCache: true);
                        if (author != null)
                        {
                            authors.Add(author);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "OpenLibrary: Error fetching author detail for {0}", doc.OlId);
                    }
                }

                return authors;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "OpenLibrary: Error searching for author '{0}'", authorName);
                return new List<Author>();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ISearchForNewEntity
        // ─────────────────────────────────────────────────────────────────────
        public List<object> SearchForNewEntity(string query)
        {
            var results = new List<object>();

            var books = SearchOpenLibrary(query, false);
            results.AddRange(books.Cast<object>());

            return results;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Private HTTP helpers
        // ─────────────────────────────────────────────────────────────────────
        private HttpRequest BuildRequest(string path, string fullUrl = null)
        {
            var url = fullUrl ?? $"{BaseUrl}{path}";
            var request = new HttpRequest(url);
            request.Headers.Set("User-Agent", UserAgent);
            return request;
        }

        private OpenLibraryAuthorResource FetchAuthor(string olId)
        {
            var httpRequest = BuildRequest($"/authors/{olId}.json");
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get(httpRequest);

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                throw new AuthorNotFoundException(olId);
            }

            if (httpResponse.HasHttpError)
            {
                throw new BookInfoException($"OpenLibrary: Unexpected error fetching author {olId}");
            }

            return JsonSerializer.Deserialize<OpenLibraryAuthorResource>(httpResponse.Content, SerializerSettings);
        }

        private OpenLibraryAuthorWorksResponse FetchAuthorWorks(string olId)
        {
            var httpRequest = BuildRequest($"/authors/{olId}/works.json?limit=50");
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get(httpRequest);

            if (httpResponse.HasHttpError)
            {
                _logger.Warn("OpenLibrary: Could not fetch works for author {0}", olId);
                return new OpenLibraryAuthorWorksResponse { Entries = new List<OpenLibraryAuthorWorkEntry>() };
            }

            return JsonSerializer.Deserialize<OpenLibraryAuthorWorksResponse>(httpResponse.Content, SerializerSettings)
                   ?? new OpenLibraryAuthorWorksResponse { Entries = new List<OpenLibraryAuthorWorkEntry>() };
        }

        private OpenLibraryWorkResource FetchWork(string olId)
        {
            var httpRequest = BuildRequest($"/works/{olId}.json");
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get(httpRequest);

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                throw new BookNotFoundException(olId);
            }

            if (httpResponse.HasHttpError)
            {
                throw new BookInfoException($"OpenLibrary: Unexpected error fetching work {olId}");
            }

            return JsonSerializer.Deserialize<OpenLibraryWorkResource>(httpResponse.Content, SerializerSettings);
        }

        private OpenLibraryEditionsResponse FetchEditions(string workOlId)
        {
            var httpRequest = BuildRequest($"/works/{workOlId}/editions.json?limit=50");
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get(httpRequest);

            if (httpResponse.HasHttpError)
            {
                _logger.Warn("OpenLibrary: Could not fetch editions for work {0}", workOlId);
                return new OpenLibraryEditionsResponse { Entries = new List<OpenLibraryEditionResource>() };
            }

            return JsonSerializer.Deserialize<OpenLibraryEditionsResponse>(httpResponse.Content, SerializerSettings)
                   ?? new OpenLibraryEditionsResponse { Entries = new List<OpenLibraryEditionResource>() };
        }

        private OpenLibraryEditionResource FetchEdition(string olId)
        {
            var httpRequest = BuildRequest($"/books/{olId}.json");
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get(httpRequest);

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                throw new EditionNotFoundException(olId);
            }

            if (httpResponse.HasHttpError)
            {
                return null;
            }

            return JsonSerializer.Deserialize<OpenLibraryEditionResource>(httpResponse.Content, SerializerSettings);
        }

        private List<Book> SearchOpenLibrary(string query, bool getAllEditions)
        {
            var fields = "key,title,author_key,author_name,first_publish_year,isbn,subject,cover_i,ratings_count,ratings_average";
            var url = $"{BaseUrl}/search.json?q={Uri.EscapeDataString(query)}&fields={fields}&limit=20";

            var httpRequest = BuildRequest(null, url);
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get(httpRequest);

            if (httpResponse.HasHttpError)
            {
                _logger.Warn("OpenLibrary: Search failed for '{0}'", query);
                return new List<Book>();
            }

            var response = JsonSerializer.Deserialize<OpenLibrarySearchResponse>(httpResponse.Content, SerializerSettings);

            if (response?.Docs == null || !response.Docs.Any())
            {
                return new List<Book>();
            }

            var books = new List<Book>();

            foreach (var doc in response.Docs.Take(10))
            {
                try
                {
                    var book = MapSearchDocToBook(doc);
                    books.Add(book);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "OpenLibrary: Error mapping search result for work {0}", doc.WorkOlId);
                }
            }

            return books;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Private mappers
        // ─────────────────────────────────────────────────────────────────────
        private static AuthorMetadata MapAuthorMetadata(OpenLibraryAuthorResource resource)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = resource.OlId,
                TitleSlug = resource.OlId,
                Name = (resource.PersonalName ?? resource.Name ?? "Unknown").CleanSpaces(),
                Overview = resource.Bio,
                Status = resource.DeathDate.IsNotNullOrWhiteSpace()
                    ? AuthorStatusType.Ended
                    : AuthorStatusType.Continuing,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            metadata.SortName = metadata.Name.ToLower();
            metadata.NameLastFirst = metadata.Name.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLower();

            if (resource.Photos != null && resource.Photos.Any())
            {
                var photoId = resource.Photos[0];
                metadata.Images.Add(new MediaCover.MediaCover
                {
                    Url = $"{CoversBaseUrl}/a/id/{photoId}-L.jpg",
                    CoverType = MediaCoverTypes.Poster
                });
            }
            else if (resource.OlId.IsNotNullOrWhiteSpace())
            {
                metadata.Images.Add(new MediaCover.MediaCover
                {
                    Url = $"{CoversBaseUrl}/a/olid/{resource.OlId}-L.jpg",
                    CoverType = MediaCoverTypes.Poster
                });
            }

            metadata.Links.Add(new Links
            {
                Url = $"https://openlibrary.org/authors/{resource.OlId}",
                Name = "Open Library"
            });

            if (resource.Wikipedia.IsNotNullOrWhiteSpace())
            {
                metadata.Links.Add(new Links { Url = resource.Wikipedia, Name = "Wikipedia" });
            }

            return metadata;
        }

        private List<Book> MapAuthorWorksToBooks(OpenLibraryAuthorWorksResponse worksResponse, AuthorMetadata metadata)
        {
            if (worksResponse?.Entries == null)
            {
                return new List<Book>();
            }

            var books = new List<Book>();

            foreach (var entry in worksResponse.Entries.Take(50))
            {
                try
                {
                    var editions = FetchEditions(entry.OlId);
                    var workResource = new OpenLibraryWorkResource
                    {
                        Key = entry.Key,
                        Title = entry.Title,
                        Covers = entry.Covers,
                        FirstPublishDate = entry.FirstPublishDate,
                        Authors = new List<OpenLibraryWorkAuthorEntry>
                        {
                            new OpenLibraryWorkAuthorEntry
                            {
                                Author = new OpenLibraryKeyRef { Key = $"/authors/{metadata.ForeignAuthorId}" }
                            }
                        }
                    };

                    var book = MapBook(workResource, editions);
                    book.AuthorMetadata = metadata;
                    books.Add(book);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "OpenLibrary: Error mapping work {0}", entry.OlId);
                }
            }

            return books;
        }

        private static Book MapBook(OpenLibraryWorkResource work, OpenLibraryEditionsResponse editions)
        {
            var book = new Book
            {
                ForeignBookId = work.OlId,
                Title = work.Title ?? "Unknown Title",
                TitleSlug = work.OlId,
                CleanTitle = Parser.Parser.CleanAuthorName(work.Title ?? ""),
                ReleaseDate = TryParsePublishDate(work.FirstPublishDate),
                Genres = work.Subjects?.Take(5).ToList() ?? new List<string>(),
                AnyEditionOk = true,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            book.Links.Add(new Links
            {
                Url = $"https://openlibrary.org/works/{work.OlId}",
                Name = "Open Library"
            });

            var editionList = editions?.Entries?.Take(MaxEditionsPerWork)
                .Select(MapEdition)
                .Where(e => e != null)
                .ToList() ?? new List<Edition>();

            if (!editionList.Any())
            {
                // Create a synthetic edition from the work itself
                var syntheticEdition = new Edition
                {
                    ForeignEditionId = work.OlId,
                    TitleSlug = work.OlId,
                    Title = work.Title ?? "Unknown Title",
                    Monitored = true
                };

                if (work.Covers != null && work.Covers.Any())
                {
                    syntheticEdition.Images.Add(new MediaCover.MediaCover
                    {
                        Url = $"{CoversBaseUrl}/b/id/{work.Covers[0]}-L.jpg",
                        CoverType = MediaCoverTypes.Cover
                    });
                }

                editionList.Add(syntheticEdition);
            }
            else
            {
                // Mark the most complete edition as monitored
                var mostComplete = editionList
                    .OrderByDescending(e => ScoreEditionCompleteness(e))
                    .First();
                mostComplete.Monitored = true;
            }

            book.Editions = editionList;

            Debug.Assert(!book.Editions.Value.Any() || book.Editions.Value.Count(x => x.Monitored) == 1,
                "one edition monitored");

            return book;
        }

        private static Edition MapEdition(OpenLibraryEditionResource resource)
        {
            if (resource?.OlId == null)
            {
                return null;
            }

            var edition = new Edition
            {
                ForeignEditionId = resource.OlId,
                TitleSlug = resource.OlId,
                Title = resource.Title ?? "Unknown Edition",
                Isbn13 = resource.Isbn13?.FirstOrDefault(),
                Publisher = resource.Publishers?.FirstOrDefault(),
                PageCount = resource.NumberOfPages ?? 0,
                Format = resource.PhysicalFormat,
                IsEbook = resource.PhysicalFormat?.ToLowerInvariant().Contains("ebook") == true
                       || resource.PhysicalFormat?.ToLowerInvariant().Contains("epub") == true,
                Language = resource.Languages?.FirstOrDefault()?.Code ?? "und",
                Overview = resource.Description,
                ReleaseDate = TryParsePublishDate(resource.PublishDate),
                Monitored = false,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            if (resource.Covers != null && resource.Covers.Any())
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = $"{CoversBaseUrl}/b/id/{resource.Covers[0]}-L.jpg",
                    CoverType = MediaCoverTypes.Cover
                });
            }

            edition.Links.Add(new Links
            {
                Url = $"https://openlibrary.org/books/{resource.OlId}",
                Name = "Open Library"
            });

            return edition;
        }

        private static Book MapSearchDocToBook(OpenLibrarySearchDoc doc)
        {
            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = doc.PrimaryAuthorOlId ?? doc.WorkOlId,
                TitleSlug = doc.PrimaryAuthorOlId ?? doc.WorkOlId,
                Name = doc.AuthorName?.FirstOrDefault() ?? "Unknown Author",
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            authorMetadata.SortName = authorMetadata.Name.ToLower();
            authorMetadata.NameLastFirst = authorMetadata.Name.ToLastFirst();
            authorMetadata.SortNameLastFirst = authorMetadata.NameLastFirst.ToLower();

            var edition = new Edition
            {
                ForeignEditionId = doc.WorkOlId,
                TitleSlug = doc.WorkOlId,
                Title = doc.Title ?? "Unknown Title",
                Isbn13 = doc.Isbn?.FirstOrDefault(i => i.Length == 13),
                Monitored = true,
                Ratings = new Ratings
                {
                    Votes = doc.RatingsCount ?? 0,
                    Value = (decimal)(doc.RatingsAverage ?? 0)
                }
            };

            if (doc.CoverId.HasValue)
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = $"{CoversBaseUrl}/b/id/{doc.CoverId.Value}-L.jpg",
                    CoverType = MediaCoverTypes.Cover
                });
            }

            edition.Links.Add(new Links
            {
                Url = $"https://openlibrary.org/works/{doc.WorkOlId}",
                Name = "Open Library"
            });

            var book = new Book
            {
                ForeignBookId = doc.WorkOlId,
                Title = doc.Title ?? "Unknown Title",
                TitleSlug = doc.WorkOlId,
                CleanTitle = Parser.Parser.CleanAuthorName(doc.Title ?? ""),
                ReleaseDate = doc.FirstPublishYear.HasValue
                    ? new DateTime(doc.FirstPublishYear.Value, 1, 1)
                    : (DateTime?)null,
                Genres = doc.Subject?.Take(5).ToList() ?? new List<string>(),
                AnyEditionOk = true,
                AuthorMetadata = authorMetadata,
                Ratings = new Ratings
                {
                    Votes = doc.RatingsCount ?? 0,
                    Value = (decimal)(doc.RatingsAverage ?? 0)
                },
                Editions = new List<Edition> { edition }
            };

            book.Links.Add(new Links
            {
                Url = $"https://openlibrary.org/works/{doc.WorkOlId}",
                Name = "Open Library"
            });

            book.Author = new Author
            {
                Metadata = authorMetadata,
                CleanName = Parser.Parser.CleanAuthorName(authorMetadata.Name)
            };

            return book;
        }

        private void AddDbIds(string authorId, Book book, Dictionary<string, AuthorMetadata> authors)
        {
            var dbBook = _bookService.FindById(book.ForeignBookId);
            if (dbBook != null)
            {
                book.UseDbFieldsFrom(dbBook);

                var editions = _editionService.GetEditionsByBook(dbBook.Id)
                    .ToDictionary(x => x.ForeignEditionId);

                foreach (var edition in book.Editions.Value)
                {
                    edition.Monitored = false;
                    if (editions.TryGetValue(edition.ForeignEditionId, out var dbEdition))
                    {
                        edition.UseDbFieldsFrom(dbEdition);
                    }
                }

                // Ensure at least one edition is monitored
                if (book.Editions.Value.Any() && !book.Editions.Value.Any(x => x.Monitored))
                {
                    book.Editions.Value
                        .OrderByDescending(x => ScoreEditionCompleteness(x))
                        .First().Monitored = true;
                }
            }

            var author = _authorService.FindById(authorId);

            if (author == null)
            {
                if (!authors.TryGetValue(authorId, out var metadata))
                {
                    metadata = new AuthorMetadata { ForeignAuthorId = authorId, Name = "Unknown Author" };
                }

                author = new Author
                {
                    CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                    Metadata = metadata
                };
            }

            book.Author = author;
            book.AuthorMetadata = author.Metadata.Value;
            book.AuthorMetadataId = author.AuthorMetadataId;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Utilities
        // ─────────────────────────────────────────────────────────────────────
        private static DateTime? TryParsePublishDate(string dateStr)
        {
            if (dateStr.IsNullOrWhiteSpace())
            {
                return null;
            }

            var formats = new[]
            {
                "yyyy",
                "MMMM yyyy",
                "MMM yyyy",
                "MMMM d, yyyy",
                "MMMM dd, yyyy",
                "d MMMM yyyy",
                "dd MMMM yyyy",
                "yyyy-MM-dd",
                "MM/dd/yyyy"
            };

            foreach (var fmt in formats)
            {
                if (DateTime.TryParseExact(
                    dateStr.Trim(),
                    fmt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var result))
                {
                    return result;
                }
            }

            if (DateTime.TryParse(
                dateStr.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var fallback))
            {
                return fallback;
            }

            return null;
        }

        private static int ScoreEditionCompleteness(Edition e)
        {
            var score = 0;
            if (e.Isbn13.IsNotNullOrWhiteSpace())
            {
                score += 10;
            }

            if (e.Publisher.IsNotNullOrWhiteSpace())
            {
                score += 5;
            }

            if (e.PageCount > 0)
            {
                score += 5;
            }

            if (e.Overview.IsNotNullOrWhiteSpace())
            {
                score += 3;
            }

            if (e.Images.Any())
            {
                score += 3;
            }

            if (e.ReleaseDate.HasValue)
            {
                score += 2;
            }

            return score;
        }
    }
}

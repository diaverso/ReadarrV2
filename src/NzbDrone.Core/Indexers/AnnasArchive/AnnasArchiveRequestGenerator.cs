using System.Collections.Generic;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.AnnasArchive
{
    public class AnnasArchiveRequestGenerator : IIndexerRequestGenerator
    {
        public AnnasArchiveSettings Settings { get; set; }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            // Anna's Archive does not support RSS
            return new IndexerPageableRequestChain();
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var query = BuildQuery(searchCriteria.BookQuery, searchCriteria.AuthorQuery);
            pageableRequests.Add(GetRequests(query));

            // Also try author + book order
            var altQuery = BuildQuery(searchCriteria.AuthorQuery, searchCriteria.BookQuery);
            if (altQuery != query)
            {
                pageableRequests.AddTier();
                pageableRequests.Add(GetRequests(altQuery));
            }

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(AuthorSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var query = searchCriteria.AuthorQuery.Replace("+", " ").Trim();
            pageableRequests.Add(GetRequests(query));

            return pageableRequests;
        }

        private string BuildQuery(string bookQuery, string authorQuery)
        {
            var book = bookQuery?.Replace("+", " ").Trim() ?? string.Empty;
            var author = authorQuery?.Replace("+", " ").Trim() ?? string.Empty;

            if (book.Length > 0 && author.Length > 0)
            {
                return $"{author} {book}";
            }

            return book.Length > 0 ? book : author;
        }

        private IEnumerable<IndexerRequest> GetRequests(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                yield break;
            }

            var url = new HttpUri(Settings.BaseUrl.TrimEnd('/') + "/search")
                .AddQueryParam("q", query)
                .AddQueryParam("output", "json");

            if (!string.IsNullOrWhiteSpace(Settings.Formats))
            {
                foreach (var fmt in Settings.Formats.Split(','))
                {
                    var f = fmt.Trim();
                    if (!string.IsNullOrWhiteSpace(f))
                    {
                        url = url.AddQueryParam("ext", f);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(Settings.Languages))
            {
                foreach (var lang in Settings.Languages.Split(','))
                {
                    var l = lang.Trim();
                    if (!string.IsNullOrWhiteSpace(l))
                    {
                        url = url.AddQueryParam("lang", l);
                    }
                }
            }

            var request = new IndexerRequest(url.FullUri, HttpAccept.Json);
            request.HttpRequest.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Readarr/1.0)");

            yield return request;
        }
    }
}

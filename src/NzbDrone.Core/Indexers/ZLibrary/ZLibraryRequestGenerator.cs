using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.ZLibrary
{
    public class ZLibraryRequestGenerator : IIndexerRequestGenerator
    {
        public ZLibrarySettings Settings { get; set; }
        public IHttpClient HttpClient { get; set; }
        public ICached<Dictionary<string, string>> AuthCache { get; set; }
        public Logger Logger { get; set; }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            // Z-Library does not support RSS/recent feed
            return new IndexerPageableRequestChain();
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var book = searchCriteria.BookQuery?.Replace("+", " ").Trim() ?? string.Empty;
            var author = searchCriteria.AuthorQuery?.Replace("+", " ").Trim() ?? string.Empty;

            var query = string.IsNullOrWhiteSpace(author)
                ? book
                : string.IsNullOrWhiteSpace(book)
                    ? author
                    : $"{author} {book}";

            if (!string.IsNullOrWhiteSpace(query))
            {
                pageableRequests.Add(BuildSearchRequest(query));
            }

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(AuthorSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var author = searchCriteria.AuthorQuery?.Replace("+", " ").Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(author))
            {
                pageableRequests.Add(BuildSearchRequest(author));
            }

            return pageableRequests;
        }

        private IEnumerable<IndexerRequest> BuildSearchRequest(string query)
        {
            Authenticate().GetAwaiter().GetResult();

            var session = AuthCache.Find(CacheKey);
            if (session == null)
            {
                Logger.Warn("Z-Library authentication failed; skipping search.");
                yield break;
            }

            var bodyParts = new List<string>
            {
                $"message={Uri.EscapeDataString(query)}",
                "page=1",
                "limit=20",
                "order=popular"
            };

            if (!string.IsNullOrWhiteSpace(Settings.Formats))
            {
                foreach (var fmt in Settings.Formats.Split(','))
                {
                    var f = fmt.Trim().ToLowerInvariant();
                    if (f.IsNotNullOrWhiteSpace())
                    {
                        bodyParts.Add($"extensions[]={Uri.EscapeDataString(f)}");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(Settings.Languages))
            {
                foreach (var lang in Settings.Languages.Split(','))
                {
                    var l = lang.Trim().ToLowerInvariant();
                    if (l.IsNotNullOrWhiteSpace())
                    {
                        bodyParts.Add($"languages[]={Uri.EscapeDataString(l)}");
                    }
                }
            }

            var searchUrl = $"{Settings.BaseUrl.TrimEnd('/')}/eapi/book/search";
            var request = new IndexerRequest(searchUrl, HttpAccept.Json);
            request.HttpRequest.Method = HttpMethod.Post;
            request.HttpRequest.SetContent(string.Join("&", bodyParts));
            request.HttpRequest.Headers.ContentType = "application/x-www-form-urlencoded";
            request.HttpRequest.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Readarr/1.0)");

            if (session.TryGetValue("userId", out var userId) && session.TryGetValue("userKey", out var userKey))
            {
                request.HttpRequest.Headers.Add("remix-userid", userId);
                request.HttpRequest.Headers.Add("remix-userkey", userKey);
            }

            yield return request;
        }

        private string CacheKey => Settings.BaseUrl.TrimEnd('/');

        private async Task Authenticate()
        {
            var cached = AuthCache.Find(CacheKey);
            if (cached != null)
            {
                return;
            }

            var loginUrl = $"{Settings.BaseUrl.TrimEnd('/')}/eapi/user/login";
            var body = $"email={Uri.EscapeDataString(Settings.Email)}&password={Uri.EscapeDataString(Settings.Password)}";

            var loginRequest = new HttpRequest(loginUrl);
            loginRequest.Method = HttpMethod.Post;
            loginRequest.SetContent(body);
            loginRequest.Headers.ContentType = "application/x-www-form-urlencoded";
            loginRequest.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Readarr/1.0)");

            HttpResponse response;
            try
            {
                response = await HttpClient.ExecuteAsync(loginRequest);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Z-Library login request failed.");
                return;
            }

            // Try to extract remix-userid and remix-userkey from cookies or JSON body
            var session = new Dictionary<string, string>();

            // Check cookies first
            var cookies = response.GetCookies();
            if (cookies.TryGetValue("remix_userid", out var uid1))
            {
                session["userId"] = uid1;
            }

            if (cookies.TryGetValue("remix_userkey", out var ukey1))
            {
                session["userKey"] = ukey1;
            }

            // Fallback: parse JSON body
            if (!session.ContainsKey("userId") && !string.IsNullOrWhiteSpace(response.Content))
            {
                try
                {
                    var loginResp = Json.Deserialize<ZLibraryLoginResponse>(response.Content);
                    if (loginResp != null)
                    {
                        var userId = loginResp.UserId ?? loginResp.RemixUserId;
                        var userKey = loginResp.Token ?? loginResp.RemixUserKey;

                        if (!string.IsNullOrWhiteSpace(userId))
                        {
                            session["userId"] = userId;
                        }

                        if (!string.IsNullOrWhiteSpace(userKey))
                        {
                            session["userKey"] = userKey;
                        }
                    }
                }
                catch
                {
                    // Ignore JSON parse errors
                }
            }

            if (session.ContainsKey("userId") && session.ContainsKey("userKey"))
            {
                // Cache session for 30 minutes
                AuthCache.Set(CacheKey, session, TimeSpan.FromMinutes(30));
                Logger.Debug("Z-Library authentication succeeded.");
            }
            else
            {
                Logger.Warn("Z-Library authentication failed: no session credentials found in response.");
            }
        }
    }
}

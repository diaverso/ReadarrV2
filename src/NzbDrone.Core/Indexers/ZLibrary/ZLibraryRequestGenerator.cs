using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.ZLibrary
{
    public class ZLibraryRequestGenerator : IIndexerRequestGenerator
    {
        private const string TorOnionUrl = "http://bookszlibb74ugqojhzhg2a63w5i2atv5bqarulgczawnbmsb6s6qead.onion";

        public ZLibrarySettings Settings { get; set; }
        public IHttpClient HttpClient { get; set; }
        public ICached<Dictionary<string, string>> AuthCache { get; set; }
        public Logger Logger { get; set; }
        public IConfigService ConfigService { get; set; }

        private bool UseTor => Settings.UseTor || ConfigService?.TorProxyEnabled == true;

        private string EffectiveBaseUrl =>
            UseTor ? TorOnionUrl : (Settings.BaseUrl?.TrimEnd('/') ?? "https://singlelogin.rs");

        private string CacheKey => EffectiveBaseUrl;

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
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

            var searchUrl = $"{EffectiveBaseUrl}/eapi/book/search";
            var request = new IndexerRequest(searchUrl, HttpAccept.Json);
            request.HttpRequest.Method = HttpMethod.Post;
            request.HttpRequest.SetContent(string.Join("&", bodyParts));
            request.HttpRequest.Headers.ContentType = "application/x-www-form-urlencoded";
            request.HttpRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            if (session.TryGetValue("cookieString", out var cookieStr))
            {
                // Raw cookie string (z-lib.cv and other Laravel-based mirrors)
                request.HttpRequest.Headers.Add("Cookie", cookieStr);
            }
            else if (session.TryGetValue("userId", out var userId) && session.TryGetValue("userKey", out var userKey))
            {
                // singlelogin.rs EAPI headers
                request.HttpRequest.Headers.Add("remix-userid", userId);
                request.HttpRequest.Headers.Add("remix-userkey", userKey);
            }

            yield return request;
        }

        private async Task Authenticate()
        {
            var cached = AuthCache.Find(CacheKey);
            if (cached != null)
            {
                return;
            }

            // Option 1: raw session cookies (e.g. z-lib.cv: "z_lib_session=...; zl_logged_in=1")
            if (!string.IsNullOrWhiteSpace(Settings.SessionCookies))
            {
                var cookieSession = new Dictionary<string, string>
                {
                    { "cookieString", Settings.SessionCookies.Trim() }
                };
                AuthCache.Set(CacheKey, cookieSession, TimeSpan.FromDays(7));
                Logger.Debug("Z-Library: using raw session cookies.");
                return;
            }

            // Option 2: singlelogin.rs remix cookies
            if (!string.IsNullOrWhiteSpace(Settings.RemixUserId) && !string.IsNullOrWhiteSpace(Settings.RemixUserKey))
            {
                var manualSession = new Dictionary<string, string>
                {
                    { "userId", Settings.RemixUserId.Trim() },
                    { "userKey", Settings.RemixUserKey.Trim() }
                };
                AuthCache.Set(CacheKey, manualSession, TimeSpan.FromDays(7));
                Logger.Debug("Z-Library: using remix session cookies (userId={0}).", Settings.RemixUserId.Trim());
                return;
            }

            var loginUrl = $"{EffectiveBaseUrl}/eapi/user/login";
            var body = $"email={Uri.EscapeDataString(Settings.Email ?? string.Empty)}&password={Uri.EscapeDataString(Settings.Password ?? string.Empty)}";

            var loginRequest = new HttpRequest(loginUrl);
            loginRequest.Method = HttpMethod.Post;
            loginRequest.SetContent(body);
            loginRequest.Headers.ContentType = "application/x-www-form-urlencoded";
            loginRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            loginRequest.Headers.Add("Accept", "application/json, text/plain, */*");
            loginRequest.Headers.Add("Accept-Language", "en-US,en;q=0.9");
            loginRequest.Headers.Add("Origin", EffectiveBaseUrl);
            loginRequest.Headers.Add("Referer", $"{EffectiveBaseUrl}/");

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

            var session = new Dictionary<string, string>();

            Logger.Warn("Z-Library login HTTP {0}, content-length={1}, content-type={2}",
                (int)response.StatusCode,
                response.Content?.Length ?? -1,
                response.Headers.ContentType ?? "null");

            // First: try cookies (some Z-Library mirrors set them)
            var cookies = response.GetCookies();
            Logger.Warn("Z-Library cookies received: [{0}]", string.Join(", ", cookies.Keys));

            if (cookies.TryGetValue("remix_userid", out var cookieUid))
            {
                session["userId"] = cookieUid;
            }

            if (cookies.TryGetValue("remix_userkey", out var cookieKey))
            {
                session["userKey"] = cookieKey;
            }

            // Second: parse JSON body if either credential is still missing
            Logger.Warn("Z-Library login body: [{0}]",
                string.IsNullOrWhiteSpace(response.Content) ? "EMPTY" :
                response.Content.Length > 800 ? response.Content.Substring(0, 800) : response.Content);

            if ((!session.ContainsKey("userId") || !session.ContainsKey("userKey")) && !string.IsNullOrWhiteSpace(response.Content))
            {
                try
                {
                    var json = JObject.Parse(response.Content);

                    // Check for validation error first
                    var topLevel = json;
                    var respObj = json["response"] as JObject;
                    if (respObj?["validationError"] != null)
                    {
                        Logger.Warn("Z-Library login failed — invalid credentials.");
                        return;
                    }

                    string userId = null;
                    string userKey = null;

                    // EAPI format: {"user": {"id": "...", "remix_userkey": "..."}}
                    var userObj = json["user"] as JObject;
                    if (userObj != null)
                    {
                        userId = userObj["id"]?.ToString()
                            ?? userObj["remix_userid"]?.ToString()
                            ?? userObj["userId"]?.ToString();
                        userKey = userObj["remix_userkey"]?.ToString()
                            ?? userObj["token"]?.ToString()
                            ?? userObj["userKey"]?.ToString();
                    }

                    // Fallback: top-level fields
                    if (string.IsNullOrWhiteSpace(userId))
                    {
                        userId = json["userId"]?.ToString()
                            ?? json["remix_userid"]?.ToString()
                            ?? json["id"]?.ToString();
                    }

                    if (string.IsNullOrWhiteSpace(userKey))
                    {
                        userKey = json["token"]?.ToString()
                            ?? json["remix_userkey"]?.ToString()
                            ?? json["userKey"]?.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(userId)) session["userId"] = userId;
                    if (!string.IsNullOrWhiteSpace(userKey)) session["userKey"] = userKey;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Z-Library: failed to parse login response.");
                }
            }

            if (session.ContainsKey("userId") && session.ContainsKey("userKey"))
            {
                AuthCache.Set(CacheKey, session, TimeSpan.FromMinutes(30));
                Logger.Debug("Z-Library authentication succeeded (userId={0}).", session["userId"]);
            }
            else
            {
                Logger.Warn("Z-Library authentication failed: no session credentials found in response.");
            }
        }
    }
}

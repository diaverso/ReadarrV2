using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Indexers.ZLibrary
{
    public class ZLibrary : HttpIndexerBase<ZLibrarySettings>
    {
        public override string Name => "Z-Library";
        public override DownloadProtocol Protocol => DownloadProtocol.Unknown;
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;
        public override int PageSize => 20;

        private readonly ICached<Dictionary<string, string>> _authCache;

        public ZLibrary(IHttpClient httpClient,
            ICacheManager cacheManager,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
            _authCache = cacheManager.GetCache<Dictionary<string, string>>(GetType(), "authSession");
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new ZLibraryRequestGenerator
            {
                Settings = Settings,
                HttpClient = _httpClient,
                AuthCache = _authCache,
                Logger = _logger,
                ConfigService = _configService,
            };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new ZLibraryParser(Settings, _authCache, _configService);
        }

        protected override async Task<ValidationFailure> TestConnection()
        {
            // If any manual cookies are provided, just verify we can reach the search endpoint
            var hasManualCookies = !string.IsNullOrWhiteSpace(Settings.SessionCookies) ||
                                   (!string.IsNullOrWhiteSpace(Settings.RemixUserId) && !string.IsNullOrWhiteSpace(Settings.RemixUserKey));
            if (hasManualCookies)
            {
                try
                {
                    const string torOnionUrl = "http://bookszlibb74ugqojhzhg2a63w5i2atv5bqarulgczawnbmsb6s6qead.onion";
                    var useTor = Settings.UseTor || _configService?.TorProxyEnabled == true;
                    var baseUrl = useTor ? torOnionUrl : (Settings.BaseUrl?.TrimEnd('/') ?? "https://singlelogin.rs");

                    var testRequest = new HttpRequest($"{baseUrl}/eapi/book/search");
                    testRequest.Method = HttpMethod.Post;
                    testRequest.SetContent("message=test&page=1&limit=1");
                    testRequest.Headers.ContentType = "application/x-www-form-urlencoded";
                    testRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    if (!string.IsNullOrWhiteSpace(Settings.SessionCookies))
                    {
                        testRequest.Headers.Add("Cookie", Settings.SessionCookies.Trim());
                    }
                    else if (!string.IsNullOrWhiteSpace(Settings.RemixUserId))
                    {
                        testRequest.Headers.Add("remix-userid", Settings.RemixUserId.Trim());
                        testRequest.Headers.Add("remix-userkey", Settings.RemixUserKey.Trim());
                    }

                    var response = await _httpClient.ExecuteAsync(testRequest);
                    if (response.HasHttpError)
                    {
                        return new ValidationFailure(string.Empty, $"Z-Library search test failed. HTTP {(int)response.StatusCode}");
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    return new ValidationFailure(string.Empty, "Unable to connect to Z-Library: " + ex.Message);
                }
            }

            // Fall back to email/password login
            const string torUrl = "http://bookszlibb74ugqojhzhg2a63w5i2atv5bqarulgczawnbmsb6s6qead.onion";
            var useTorLogin = Settings.UseTor || _configService?.TorProxyEnabled == true;
            var loginBaseUrl = useTorLogin ? torUrl : (Settings.BaseUrl?.TrimEnd('/') ?? "https://singlelogin.rs");
            _authCache.Remove(loginBaseUrl);

            try
            {
                var loginUrl = $"{loginBaseUrl}/eapi/user/login";
                var body = $"email={Uri.EscapeDataString(Settings.Email ?? string.Empty)}&password={Uri.EscapeDataString(Settings.Password ?? string.Empty)}";

                var loginRequest = new HttpRequest(loginUrl);
                loginRequest.Method = HttpMethod.Post;
                loginRequest.SetContent(body);
                loginRequest.Headers.ContentType = "application/x-www-form-urlencoded";
                loginRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                loginRequest.Headers.Add("Accept", "application/json, text/plain, */*");

                var response = await _httpClient.ExecuteAsync(loginRequest);
                if (response.HasHttpError)
                {
                    return new ValidationFailure(string.Empty, $"Unable to connect to Z-Library. HTTP {(int)response.StatusCode}");
                }

                var content = response.Content ?? string.Empty;
                if (content.Contains("validationError"))
                {
                    return new ValidationFailure("Email", "Z-Library authentication failed. Check your email and password.");
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    return new ValidationFailure("RemixUserId",
                        "Z-Library login returned empty response (likely Cloudflare). " +
                        "Please use Remix User ID + Remix User Key instead: open Z-Library in your browser, go to DevTools → Application → Cookies and copy 'remix_userid' and 'remix_userkey'.");
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Z-Library test connection failed");
                return new ValidationFailure(string.Empty, "Unable to connect to Z-Library: " + ex.Message);
            }
        }
    }
}

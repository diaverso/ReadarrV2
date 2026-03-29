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
            return new ZLibraryParser(Settings);
        }

        protected override async Task<ValidationFailure> TestConnection()
        {
            // Clear cached session so credentials are re-validated on test
            _authCache.Remove(Settings.BaseUrl.TrimEnd('/'));

            try
            {
                var loginUrl = $"{Settings.BaseUrl.TrimEnd('/')}/eapi/user/login";
                var body = $"email={Uri.EscapeDataString(Settings.Email ?? string.Empty)}&password={Uri.EscapeDataString(Settings.Password ?? string.Empty)}";

                var loginRequest = new HttpRequest(loginUrl);
                loginRequest.Method = HttpMethod.Post;
                loginRequest.SetContent(body);
                loginRequest.Headers.ContentType = "application/x-www-form-urlencoded";
                loginRequest.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Readarr/1.0)");

                var response = await _httpClient.ExecuteAsync(loginRequest);

                if (response.HasHttpError)
                {
                    return new ValidationFailure(string.Empty, $"Unable to connect to Z-Library. HTTP {(int)response.StatusCode}");
                }

                var content = response.Content ?? string.Empty;
                if (content.Contains("\"error\"") || content.Contains("\"ok\":0") || content.Contains("\"ok\": 0"))
                {
                    return new ValidationFailure("Email", "Z-Library authentication failed. Check your email and password.");
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

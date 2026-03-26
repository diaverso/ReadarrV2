using System.Collections.Generic;
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
            };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new ZLibraryParser(Settings);
        }

        protected override async Task Test(List<ValidationFailure> failures)
        {
            // Clear cached session so credentials are re-validated on test
            _authCache.Remove(Settings.BaseUrl.TrimEnd('/'));
            await base.Test(failures);
        }
    }
}

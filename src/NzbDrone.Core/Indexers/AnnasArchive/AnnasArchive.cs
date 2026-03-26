using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Indexers.AnnasArchive
{
    public class AnnasArchive : HttpIndexerBase<AnnasArchiveSettings>
    {
        public override string Name => "Anna's Archive";
        public override DownloadProtocol Protocol => DownloadProtocol.Unknown;
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;
        public override int PageSize => 20;

        public AnnasArchive(IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new AnnasArchiveRequestGenerator { Settings = Settings };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new AnnasArchiveParser(Settings);
        }
    }
}

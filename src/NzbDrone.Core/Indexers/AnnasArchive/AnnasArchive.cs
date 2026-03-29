using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentValidation.Results;
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
            return new AnnasArchiveParser(Settings, _httpClient);
        }

        protected override async Task<ValidationFailure> TestConnection()
        {
            try
            {
                var url = Settings.BaseUrl.TrimEnd('/') + "/search?q=test&output=json";
                var request = new HttpRequest(url);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Readarr/1.0)");
                var response = await _httpClient.ExecuteAsync(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return new ValidationFailure(string.Empty, $"Unable to connect to Anna's Archive. HTTP {(int)response.StatusCode}");
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Anna's Archive test connection failed");
                return new ValidationFailure(string.Empty, "Unable to connect to Anna's Archive: " + ex.Message);
            }
        }
    }
}

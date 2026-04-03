using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;

namespace NzbDrone.Core.Download.Clients.HttpBlackhole
{
    public class HttpBlackholeSettingsValidator : AbstractValidator<HttpBlackholeSettings>
    {
        public HttpBlackholeSettingsValidator()
        {
            RuleFor(c => c.DownloadFolder).IsValidPath();
        }
    }

    public class HttpBlackholeSettings : IProviderConfig
    {
        private static readonly HttpBlackholeSettingsValidator Validator = new HttpBlackholeSettingsValidator();

        public HttpBlackholeSettings()
        {
            DownloadFolder = "/downloads";
            FlareSolverrUrl = string.Empty;
        }

        [FieldDefinition(0, Label = "Download Folder", Type = FieldType.Path, HelpText = "Folder where directly downloaded book files will be saved.")]
        public string DownloadFolder { get; set; }

        [FieldDefinition(1, Label = "FlareSolverr URL", Type = FieldType.Url, HelpText = "Optional. URL of a running FlareSolverr instance (e.g. http://localhost:8191). Used to bypass DDoS-Guard and Cloudflare challenges automatically. Leave empty to use the built-in PoW solver instead.", Advanced = true)]
        public string FlareSolverrUrl { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}

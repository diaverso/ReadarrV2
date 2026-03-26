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

        [FieldDefinition(0, Label = "Download Folder", Type = FieldType.Path, HelpText = "Folder where directly downloaded book files will be saved.")]
        public string DownloadFolder { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}

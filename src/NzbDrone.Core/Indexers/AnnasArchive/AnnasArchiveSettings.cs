using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.AnnasArchive
{
    public class AnnasArchiveSettingsValidator : AbstractValidator<AnnasArchiveSettings>
    {
        public AnnasArchiveSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
        }
    }

    public class AnnasArchiveSettings : IIndexerSettings
    {
        private static readonly AnnasArchiveSettingsValidator Validator = new AnnasArchiveSettingsValidator();

        public AnnasArchiveSettings()
        {
            BaseUrl = "https://annas-archive.gl";
            Formats = "epub,pdf,mobi,azw3";
            Languages = "";
        }

        [FieldDefinition(0, Label = "URL", Advanced = true, HelpText = "Anna's Archive base URL. Change only if using a mirror.")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "Formats", HelpText = "Comma-separated file formats to search (epub,pdf,mobi,azw3).", Advanced = true)]
        public string Formats { get; set; }

        [FieldDefinition(2, Label = "Languages", HelpText = "Comma-separated language codes (e.g. en,es,fr). Leave empty for all languages.", Advanced = true)]
        public string Languages { get; set; }

        [FieldDefinition(3, Label = "API Key", Type = FieldType.Password, Privacy = PrivacyLevel.ApiKey, HelpText = "Anna's Archive member API key for fast direct download URLs. Leave empty to use standard MD5 book page links.", Advanced = true)]
        public string ApiKey { get; set; }

        [FieldDefinition(4, Type = FieldType.Number, Label = "Early Download Limit", Unit = "days", HelpText = "Time before release date Readarr will download from this indexer, empty is no limit.", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}

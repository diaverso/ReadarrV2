using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.ZLibrary
{
    public class ZLibrarySettingsValidator : AbstractValidator<ZLibrarySettings>
    {
        public ZLibrarySettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.Email).NotEmpty();
            RuleFor(c => c.Password).NotEmpty();
        }
    }

    public class ZLibrarySettings : IIndexerSettings
    {
        private static readonly ZLibrarySettingsValidator Validator = new ZLibrarySettingsValidator();

        public ZLibrarySettings()
        {
            BaseUrl = "http://zlibrary24tuxziyiyfr7zd46ytefdqbqd2axkmxm4o5374ptpc52fad.onion";
            Formats = "epub,pdf,mobi,azw3";
            Languages = "english";
        }

        [FieldDefinition(0, Label = "API URL", Advanced = true, HelpText = "Z-Library API base URL. Defaults to the Tor onion address — requires Tor running on 127.0.0.1:9050 and Tor Proxy enabled in Settings → General. Alternatively use https://singlelogin.re for clearnet access.")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "Email", Privacy = PrivacyLevel.UserName, HelpText = "Your Z-Library / singlelogin.re account email.")]
        public string Email { get; set; }

        [FieldDefinition(2, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Z-Library / singlelogin.re account password.")]
        public string Password { get; set; }

        [FieldDefinition(3, Label = "Formats", HelpText = "Comma-separated file formats (epub,pdf,mobi,azw3).", Advanced = true)]
        public string Formats { get; set; }

        [FieldDefinition(4, Label = "Languages", HelpText = "Comma-separated language names (english,spanish,french). Leave empty for all.", Advanced = true)]
        public string Languages { get; set; }

        [FieldDefinition(5, Type = FieldType.Number, Label = "Early Download Limit", Unit = "days", HelpText = "Time before release date Readarr will download from this indexer, empty is no limit.", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}

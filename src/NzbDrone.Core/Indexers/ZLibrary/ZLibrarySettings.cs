using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.ZLibrary
{
    public class ZLibrarySettingsValidator : AbstractValidator<ZLibrarySettings>
    {
        public ZLibrarySettingsValidator()
        {
            // BaseUrl is optional — code falls back to default if null
        }
    }

    public class ZLibrarySettings : IIndexerSettings
    {
        private static readonly ZLibrarySettingsValidator Validator = new ZLibrarySettingsValidator();

        public ZLibrarySettings()
        {
            BaseUrl = "https://singlelogin.rs";
            Formats = "epub,pdf,mobi,azw3";
            Languages = "english";
            UseTor = false;
        }

        [FieldDefinition(0, Label = "Session Cookies", Privacy = PrivacyLevel.Password,
            HelpText = "Paste your Z-Library session cookies. Firefox: F12 > Storage > Cookies. Format: name=value; name2=value2. For z-lib.cv use: z_lib_session=VALUE; zl_logged_in=1")]
        public string SessionCookies { get; set; }

        [FieldDefinition(1, Label = "Remix User ID", Privacy = PrivacyLevel.UserName, Advanced = true,
            HelpText = "For singlelogin.rs only: your remix_userid cookie value.")]
        public string RemixUserId { get; set; }

        [FieldDefinition(2, Label = "Remix User Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password, Advanced = true,
            HelpText = "For singlelogin.rs only: your remix_userkey cookie value.")]
        public string RemixUserKey { get; set; }

        [FieldDefinition(3, Label = "Email", Privacy = PrivacyLevel.UserName, Advanced = true, HelpText = "Z-Library account email (fallback login).")]
        public string Email { get; set; }

        [FieldDefinition(4, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password, Advanced = true, HelpText = "Z-Library account password (fallback login).")]
        public string Password { get; set; }

        [FieldDefinition(5, Label = "Use Tor", Type = FieldType.Checkbox, HelpText = "Route Z-Library requests through the Tor network.", Advanced = true)]
        public bool UseTor { get; set; }

        [FieldDefinition(6, Label = "API URL", Advanced = true, HelpText = "Z-Library base URL (e.g. https://singlelogin.rs, https://z-lib.id).")]
        public string BaseUrl { get; set; }

        [FieldDefinition(7, Label = "Formats", HelpText = "Comma-separated file formats (epub,pdf,mobi,azw3).", Advanced = true)]
        public string Formats { get; set; }

        [FieldDefinition(8, Label = "Languages", HelpText = "Comma-separated language names (english,spanish,french). Leave empty for all.", Advanced = true)]
        public string Languages { get; set; }

        [FieldDefinition(9, Type = FieldType.Number, Label = "Early Download Limit", Unit = "days", HelpText = "Time before release date Readarr will download from this indexer, empty is no limit.", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}

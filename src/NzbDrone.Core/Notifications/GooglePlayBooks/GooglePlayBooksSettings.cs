using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.GooglePlayBooks
{
    public class GooglePlayBooksSettingsValidator : AbstractValidator<GooglePlayBooksSettings>
    {
        public GooglePlayBooksSettingsValidator()
        {
            RuleFor(c => c.ClientId).NotEmpty();
            RuleFor(c => c.ClientSecret).NotEmpty();
            RuleFor(c => c.RefreshToken).NotEmpty();
        }
    }

    public class GooglePlayBooksSettings : IProviderConfig
    {
        private static readonly GooglePlayBooksSettingsValidator Validator = new GooglePlayBooksSettingsValidator();

        [FieldDefinition(0, Label = "Client ID", HelpText = "OAuth2 Client ID from Google Cloud Console (Books API must be enabled). Create credentials of type 'Desktop app'.")]
        public string ClientId { get; set; }

        [FieldDefinition(1, Label = "Client Secret", Type = FieldType.Password, Privacy = PrivacyLevel.ApiKey, HelpText = "OAuth2 Client Secret from Google Cloud Console.")]
        public string ClientSecret { get; set; }

        [FieldDefinition(2, Label = "Refresh Token", Type = FieldType.Password, Privacy = PrivacyLevel.ApiKey, HelpText = "OAuth2 Refresh Token. Obtain one by authorizing with scope 'https://www.googleapis.com/auth/books' using your Client ID and Secret.")]
        public string RefreshToken { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}

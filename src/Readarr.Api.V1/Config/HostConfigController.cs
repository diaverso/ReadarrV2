using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Config
{
    [V1ApiController("config/host")]
    public class HostConfigController : RestController<HostConfigResource>
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private readonly IConfigFileProvider _configFileProvider;
        private readonly IConfigService _configService;
        private readonly IUserService _userService;

        public HostConfigController(IConfigFileProvider configFileProvider,
                                    IConfigService configService,
                                    IUserService userService,
                                    FileExistsValidator fileExistsValidator)
        {
            _configFileProvider = configFileProvider;
            _configService = configService;
            _userService = userService;

            SharedValidator.RuleFor(c => c.BindAddress)
                           .ValidIpAddress()
                           .When(c => c.BindAddress != "*" && c.BindAddress != "localhost");

            SharedValidator.RuleFor(c => c.Port).ValidPort();

            SharedValidator.RuleFor(c => c.UrlBase).ValidUrlBase();
            SharedValidator.RuleFor(c => c.InstanceName).ContainsReadarr().When(c => c.InstanceName.IsNotNullOrWhiteSpace());

            SharedValidator.RuleFor(c => c.Username).NotEmpty().When(c => c.AuthenticationMethod == AuthenticationType.Basic ||
                                                                          c.AuthenticationMethod == AuthenticationType.Forms);
            SharedValidator.RuleFor(c => c.Password).NotEmpty().When(c => c.AuthenticationMethod == AuthenticationType.Basic ||
                                                                          c.AuthenticationMethod == AuthenticationType.Forms);

            SharedValidator.RuleFor(c => c.PasswordConfirmation)
                .Must((resource, p) => IsMatchingPassword(resource)).WithMessage("Must match Password");

            SharedValidator.RuleFor(c => c.SslPort).ValidPort().When(c => c.EnableSsl);
            SharedValidator.RuleFor(c => c.SslPort).NotEqual(c => c.Port).When(c => c.EnableSsl);

            SharedValidator.RuleFor(c => c.SslCertPath)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .IsValidPath()
                .SetValidator(fileExistsValidator)
                .Must((resource, path) => IsValidSslCertificate(resource)).WithMessage("Invalid SSL certificate file or password")
                .When(c => c.EnableSsl);

            SharedValidator.RuleFor(c => c.Branch).NotEmpty().WithMessage("Branch name is required, 'master' is the default");
            SharedValidator.RuleFor(c => c.UpdateScriptPath).IsValidPath().When(c => c.UpdateMechanism == UpdateMechanism.Script);

            SharedValidator.RuleFor(c => c.BackupFolder).IsValidPath().When(c => Path.IsPathRooted(c.BackupFolder));
            SharedValidator.RuleFor(c => c.BackupInterval).InclusiveBetween(1, 7);
            SharedValidator.RuleFor(c => c.BackupRetention).InclusiveBetween(1, 90);
        }

        private bool IsValidSslCertificate(HostConfigResource resource)
        {
            X509Certificate2 cert;
            try
            {
                cert = X509CertificateLoader.LoadPkcs12FromFile(resource.SslCertPath, resource.SslCertPassword, X509KeyStorageFlags.DefaultKeySet);
            }
            catch
            {
                return false;
            }

            return cert != null;
        }

        private bool IsMatchingPassword(HostConfigResource resource)
        {
            var user = _userService.FindUser();

            if (user != null && user.Password == resource.Password)
            {
                return true;
            }

            if (resource.Password == resource.PasswordConfirmation)
            {
                return true;
            }

            return false;
        }

        // When the frontend sends only auth-related fields (e.g. from the first-run modal),
        // unset fields default to 0/null and fail validation. Fill them in from the current
        // config so that partial PUT requests are accepted.
        protected override void ValidateResource(HostConfigResource resource, bool skipValidate = false, bool skipSharedValidate = false)
        {
            if (resource != null && Request.Method == "PUT")
            {
                if (resource.Id == 0) resource.Id = 1;
                if (resource.Port == 0) resource.Port = _configFileProvider.Port;
                if (string.IsNullOrWhiteSpace(resource.BindAddress)) resource.BindAddress = _configFileProvider.BindAddress;
                if (string.IsNullOrWhiteSpace(resource.Branch)) resource.Branch = _configFileProvider.Branch;
                if (resource.BackupInterval == 0) resource.BackupInterval = _configService.BackupInterval;
                if (resource.BackupRetention == 0) resource.BackupRetention = _configService.BackupRetention;
            }

            base.ValidateResource(resource, skipValidate, skipSharedValidate);
        }

        protected override HostConfigResource GetResourceById(int id)
        {
            return GetHostConfig();
        }

        [HttpGet]
        public HostConfigResource GetHostConfig()
        {
            var resource = HostConfigResourceMapper.ToResource(_configFileProvider, _configService);
            resource.Id = 1;

            var user = _userService.FindUser();

            resource.Username = user?.Username ?? string.Empty;
            resource.Password = user?.Password ?? string.Empty;
            resource.PasswordConfirmation = string.Empty;

            return resource;
        }

        [RestPutById]
        public async global::System.Threading.Tasks.Task<ActionResult<HostConfigResource>> SaveHostConfig(HostConfigResource resource)
        {
            // ASP.NET model binding may produce a default-valued resource when it cannot read
            // the request body synchronously (AllowSynchronousIO is disabled by default in
            // Kestrel). Re-read the body asynchronously — EnableBuffering() in
            // BufferingMiddleware makes the stream seekable — and parse it with STJson so
            // that auth fields are not silently discarded.
            if (Request.Body.CanSeek)
            {
                Request.Body.Seek(0, global::System.IO.SeekOrigin.Begin);
                using var reader = new global::System.IO.StreamReader(Request.Body, leaveOpen: true);
                var rawBody = await reader.ReadToEndAsync();
                Request.Body.Seek(0, global::System.IO.SeekOrigin.Begin);

                if (!string.IsNullOrEmpty(rawBody))
                {
                    try
                    {
                        var parsed = NzbDrone.Common.Serializer.STJson.Deserialize<HostConfigResource>(rawBody);
                        if (parsed != null)
                        {
                            if (parsed.AuthenticationMethod != AuthenticationType.None)
                                resource.AuthenticationMethod = parsed.AuthenticationMethod;
                            if (parsed.AuthenticationRequired != 0)
                                resource.AuthenticationRequired = parsed.AuthenticationRequired;
                            if (parsed.Username != null)
                                resource.Username = parsed.Username;
                            if (parsed.Password != null)
                                resource.Password = parsed.Password;
                            if (parsed.PasswordConfirmation != null)
                                resource.PasswordConfirmation = parsed.PasswordConfirmation;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("SaveHostConfig: failed to re-parse request body: {0}", ex.Message);
                    }
                }
            }

            var dictionary = resource.GetType()
                                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                     .Where(prop => prop.GetValue(resource, null) != null)
                                     .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

            _configFileProvider.SaveConfigDictionary(dictionary);
            _configService.SaveConfigDictionary(dictionary);

            // Explicitly persist auth fields — SaveConfigDictionary may skip unchanged values.
            _configFileProvider.SetValue("AuthenticationMethod", resource.AuthenticationMethod);
            _configFileProvider.SetValue("AuthenticationRequired", resource.AuthenticationRequired);

            if (resource.Username.IsNotNullOrWhiteSpace() && resource.Password.IsNotNullOrWhiteSpace())
            {
                _userService.Upsert(resource.Username, resource.Password);
            }

            return Accepted(resource.Id);
        }
    }
}

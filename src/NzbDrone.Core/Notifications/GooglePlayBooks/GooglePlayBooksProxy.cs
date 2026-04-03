using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using HttpMethod = System.Net.Http.HttpMethod;

namespace NzbDrone.Core.Notifications.GooglePlayBooks
{
    public interface IGooglePlayBooksProxy
    {
        void TestConnection(GooglePlayBooksSettings settings);
        void UploadBook(string filePath, GooglePlayBooksSettings settings);
    }

    public class GooglePlayBooksProxy : IGooglePlayBooksProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        private const string TokenUrl = "https://oauth2.googleapis.com/token";

        // Google Drive API — uploading an EPUB/PDF to Drive makes it appear in Play Books automatically.
        // The old Books API /useruploadedbooks endpoint was deprecated and returns 404.
        private const string DriveUploadUrl = "https://www.googleapis.com/upload/drive/v3/files";
        private const string DriveAboutUrl = "https://www.googleapis.com/drive/v3/about?fields=user";

        public GooglePlayBooksProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void TestConnection(GooglePlayBooksSettings settings)
        {
            var token = GetAccessToken(settings);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new Exception("Failed to obtain access token from Google. Verify Client ID, Client Secret, and Refresh Token.");
            }

            // Probe the Drive API to confirm scope and connectivity
            var probe = new HttpRequest(DriveAboutUrl);
            probe.Headers.Add("Authorization", $"Bearer {token}");
            probe.SuppressHttpError = true;
            var resp = _httpClient.Execute(probe);
            if (resp.HasHttpError)
            {
                throw new Exception(
                    $"Google Drive API returned {(int)resp.StatusCode}. " +
                    "Make sure you enabled the Google Drive API and obtained the refresh token with scope https://www.googleapis.com/auth/drive.file");
            }
        }

        public void UploadBook(string filePath, GooglePlayBooksSettings settings)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            // Google Play Books only shows EPUB and PDF files from Drive
            if (ext != ".epub" && ext != ".pdf")
            {
                _logger.Debug("Skipping {0}: Google Play Books only accepts EPUB and PDF files", Path.GetFileName(filePath));
                return;
            }

            var fileName = Path.GetFileName(filePath);
            _logger.Info("Uploading {0} to Google Drive (Play Books)", fileName);

            var accessToken = GetAccessToken(settings);
            var mimeType = ext == ".epub" ? "application/epub+zip" : "application/pdf";
            var fileBytes = File.ReadAllBytes(filePath);

            // Probe Drive API before uploading to get a meaningful error instead of "Broken pipe"
            var probe = new HttpRequest(DriveAboutUrl);
            probe.Headers.Add("Authorization", $"Bearer {accessToken}");
            probe.SuppressHttpError = true;
            HttpResponse probeResponse;
            try
            {
                probeResponse = _httpClient.Execute(probe);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Cannot reach Google Drive API: {ex.Message}. " +
                    "Check internet access to googleapis.com and SSL certificate trust.");
            }

            if (probeResponse.HasHttpError)
            {
                var body = probeResponse.Content ?? string.Empty;
                throw new Exception(
                    $"Google Drive API denied access ({(int)probeResponse.StatusCode}): {body.Substring(0, Math.Min(body.Length, 400))}. " +
                    "Enable the Drive API at https://console.cloud.google.com/apis/library/drive.googleapis.com " +
                    "and regenerate the refresh token with scope https://www.googleapis.com/auth/drive.file");
            }

            // Build multipart/related body: JSON metadata + binary file
            // This sets the filename and MIME type so Play Books recognises the book.
            var boundary = "readarr_" + Guid.NewGuid().ToString("N");
            var metadataJson = $"{{\"name\":{JsonSerializer.Serialize(fileName)},\"mimeType\":\"{mimeType}\"}}";
            var metadataBytes = Encoding.UTF8.GetBytes(metadataJson);

            byte[] multipartBody;
            using (var ms = new MemoryStream())
            {
                void WriteAscii(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }

                WriteAscii($"--{boundary}\r\n");
                WriteAscii("Content-Type: application/json; charset=UTF-8\r\n\r\n");
                ms.Write(metadataBytes, 0, metadataBytes.Length);
                WriteAscii($"\r\n--{boundary}\r\n");
                WriteAscii($"Content-Type: {mimeType}\r\n\r\n");
                ms.Write(fileBytes, 0, fileBytes.Length);
                WriteAscii($"\r\n--{boundary}--\r\n");
                multipartBody = ms.ToArray();
            }

            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            var uploadUri = new Uri($"{DriveUploadUrl}?uploadType=multipart");
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, uploadUri);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            requestMessage.Content = new ByteArrayContent(multipartBody);
            requestMessage.Content.Headers.ContentType =
                MediaTypeHeaderValue.Parse($"multipart/related; boundary=\"{boundary}\"");

            HttpResponseMessage response;
            try
            {
                response = Task.Run(() => httpClient.SendAsync(requestMessage)).GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Google Drive upload network error: {ex.Message}" +
                    (ex.InnerException != null ? $" ({ex.InnerException.Message})" : string.Empty));
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
                throw new Exception($"Google Drive upload failed ({(int)response.StatusCode}): {body}");
            }

            _logger.Info("Successfully uploaded {0} to Google Drive (Play Books)", fileName);
        }

        private string GetAccessToken(GooglePlayBooksSettings settings)
        {
            var formBody = $"grant_type=refresh_token" +
                           $"&client_id={Uri.EscapeDataString(settings.ClientId)}" +
                           $"&client_secret={Uri.EscapeDataString(settings.ClientSecret)}" +
                           $"&refresh_token={Uri.EscapeDataString(settings.RefreshToken)}";

            var request = new HttpRequest(TokenUrl)
            {
                Method = HttpMethod.Post
            };
            request.SetContent(formBody);
            request.Headers.ContentType = "application/x-www-form-urlencoded";

            var response = _httpClient.Execute(request);

            var json = JsonSerializer.Deserialize<JsonElement>(response.Content);
            if (!json.TryGetProperty("access_token", out var tokenEl))
            {
                throw new Exception($"Google OAuth2 token response did not contain access_token: {response.Content}");
            }

            return tokenEl.GetString();
        }
    }
}

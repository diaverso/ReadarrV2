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
        void UploadBook(string filePath, string authorName, string seriesName, GooglePlayBooksSettings settings);
    }

    public class GooglePlayBooksProxy : IGooglePlayBooksProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        private const string TokenUrl = "https://oauth2.googleapis.com/token";

        // Google Drive API — uploading an EPUB/PDF to Drive makes it appear in Play Books automatically.
        private const string DriveUploadUrl = "https://www.googleapis.com/upload/drive/v3/files";
        private const string DriveFilesUrl = "https://www.googleapis.com/drive/v3/files";
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

        public void UploadBook(string filePath, string authorName, string seriesName, GooglePlayBooksSettings settings)
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

            // Resolve folder hierarchy: Books/{Author}[/{Series}]
            var targetFolderId = ResolveTargetFolder(authorName, seriesName, accessToken);

            // Build multipart/related body: JSON metadata + binary file
            var boundary = "readarr_" + Guid.NewGuid().ToString("N");
            var metadataObj = $"{{\"name\":{JsonSerializer.Serialize(fileName)},\"mimeType\":\"{mimeType}\",\"parents\":[\"{targetFolderId}\"]}}";
            var metadataBytes = Encoding.UTF8.GetBytes(metadataObj);

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

            _logger.Info("Successfully uploaded {0} to Google Drive (Play Books) in folder {1}",
                fileName, string.IsNullOrWhiteSpace(seriesName) ? authorName : $"{authorName}/{seriesName}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Drive folder helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves (creating as needed) the path Books/{author}[/{series}]
        /// and returns the ID of the innermost folder.
        /// </summary>
        private string ResolveTargetFolder(string authorName, string seriesName, string accessToken)
        {
            var booksRootId = GetOrCreateFolder("Books", null, accessToken);
            var authorFolderId = GetOrCreateFolder(SanitizeFolderName(authorName ?? "Unknown Author"), booksRootId, accessToken);

            if (!string.IsNullOrWhiteSpace(seriesName))
            {
                return GetOrCreateFolder(SanitizeFolderName(seriesName), authorFolderId, accessToken);
            }

            return authorFolderId;
        }

        /// <summary>
        /// Returns the Drive folder ID for a folder with the given name under parentId
        /// (or at root if parentId is null). Creates the folder if it does not exist.
        /// </summary>
        private string GetOrCreateFolder(string name, string parentId, string accessToken)
        {
            // Search for existing folder
            var parentClause = parentId != null ? $"'{parentId}' in parents and " : "";
            var query = $"{parentClause}name={JsonSerializer.Serialize(name)} and mimeType='application/vnd.google-apps.folder' and trashed=false";
            var searchUrl = $"{DriveFilesUrl}?q={Uri.EscapeDataString(query)}&fields=files(id)&spaces=drive";

            var searchReq = new HttpRequest(searchUrl);
            searchReq.Headers.Add("Authorization", $"Bearer {accessToken}");
            searchReq.SuppressHttpError = true;
            var searchResp = _httpClient.Execute(searchReq);

            if (!searchResp.HasHttpError)
            {
                var searchJson = JsonSerializer.Deserialize<JsonElement>(searchResp.Content);
                if (searchJson.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
                {
                    return files[0].GetProperty("id").GetString();
                }
            }

            // Create folder
            var parentsJson = parentId != null ? $"[\"{parentId}\"]" : "[]";
            var createBody = $"{{\"name\":{JsonSerializer.Serialize(name)},\"mimeType\":\"application/vnd.google-apps.folder\",\"parents\":{parentsJson}}}";

            var createReq = new HttpRequest(DriveFilesUrl)
            {
                Method = HttpMethod.Post
            };
            createReq.Headers.Add("Authorization", $"Bearer {accessToken}");
            createReq.SetContent(createBody);
            createReq.Headers.ContentType = "application/json";

            var createResp = _httpClient.Execute(createReq);

            if (createResp.HasHttpError)
            {
                throw new Exception($"Failed to create Google Drive folder '{name}': {createResp.Content?.Substring(0, Math.Min(createResp.Content?.Length ?? 0, 300))}");
            }

            var createJson = JsonSerializer.Deserialize<JsonElement>(createResp.Content);
            return createJson.GetProperty("id").GetString();
        }

        private static string SanitizeFolderName(string name)
        {
            // Drive accepts most characters; just remove control characters and trim
            return (name ?? "Unknown").Trim()
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Replace('\t', ' ');
        }

        // ─────────────────────────────────────────────────────────────────────
        // OAuth2
        // ─────────────────────────────────────────────────────────────────────

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

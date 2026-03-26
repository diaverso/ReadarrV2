using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Core.Indexers.ZLibrary
{
    public class ZLibraryBook
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }

        // EAPI search returns "name"; detail endpoint returns "title"
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        // EAPI returns "authors" as array of objects with "author" field;
        // detail endpoint may return plain "author" string
        [JsonProperty("authors")]
        public JArray Authors { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("year")]
        public string Year { get; set; }

        [JsonProperty("publisher")]
        public string Publisher { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("extension")]
        public string Extension { get; set; }

        // Search result uses "size" (string like "1 Mb"); may also have numeric "filesize"
        [JsonProperty("size")]
        public string SizeString { get; set; }

        [JsonProperty("filesize")]
        public long Filesize { get; set; }

        [JsonProperty("cover")]
        public string Cover { get; set; }

        [JsonProperty("isbn")]
        public string Isbn { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        // From book detail endpoint
        [JsonProperty("download_url")]
        public string DownloadUrl { get; set; }

        // Helpers
        public string GetTitle() => !string.IsNullOrWhiteSpace(Name) ? Name : Title;

        public string GetAuthor()
        {
            if (!string.IsNullOrWhiteSpace(Author))
            {
                return Author;
            }

            if (Authors != null && Authors.Count > 0)
            {
                var first = Authors[0];
                return first["author"]?.ToString() ?? first.ToString();
            }

            return string.Empty;
        }
    }

    public class ZLibraryLoginResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("userId")]
        public string UserId { get; set; }

        // EAPI fields
        [JsonProperty("remix_userid")]
        public string RemixUserId { get; set; }

        [JsonProperty("remix_userkey")]
        public string RemixUserKey { get; set; }
    }

    public class ZLibrarySearchResponse
    {
        [JsonProperty("books")]
        public ZLibraryBook[] Books { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }
}

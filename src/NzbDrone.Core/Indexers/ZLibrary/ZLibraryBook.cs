using Newtonsoft.Json;

namespace NzbDrone.Core.Indexers.ZLibrary
{
    public class ZLibraryBook
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        // The EAPI uses "author" as a plain string; the HTML-based library uses "authors" array
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

        [JsonProperty("filesize")]
        public long Filesize { get; set; }

        [JsonProperty("filesizeString")]
        public string FilesizeString { get; set; }

        [JsonProperty("cover")]
        public string Cover { get; set; }

        [JsonProperty("isbn")]
        public string Isbn { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
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

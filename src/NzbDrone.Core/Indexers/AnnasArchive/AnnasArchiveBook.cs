using Newtonsoft.Json;

namespace NzbDrone.Core.Indexers.AnnasArchive
{
    public class AnnasArchiveBook
    {
        [JsonProperty("md5")]
        public string Md5 { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

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

        [JsonProperty("cover_url")]
        public string CoverUrl { get; set; }

        [JsonProperty("isbn")]
        public string Isbn { get; set; }
    }
}

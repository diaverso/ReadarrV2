using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    /// <summary>
    /// Maps to: GET https://openlibrary.org/search.json?q={query}&amp;fields=...&amp;limit=20
    /// Example: https://openlibrary.org/search.json?q=dune&amp;fields=key,title,author_key,author_name,first_publish_year,isbn,subject,cover_i,ratings_count,ratings_average&amp;limit=20
    /// </summary>
    public class OpenLibrarySearchResponse
    {
        [JsonPropertyName("numFound")]
        public int NumFound { get; set; }

        [JsonPropertyName("docs")]
        public List<OpenLibrarySearchDoc> Docs { get; set; }
    }

    public class OpenLibrarySearchDoc
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("author_key")]
        public List<string> AuthorKey { get; set; }

        [JsonPropertyName("author_name")]
        public List<string> AuthorName { get; set; }

        [JsonPropertyName("first_publish_year")]
        public int? FirstPublishYear { get; set; }

        [JsonPropertyName("isbn")]
        public List<string> Isbn { get; set; }

        [JsonPropertyName("subject")]
        public List<string> Subject { get; set; }

        [JsonPropertyName("cover_i")]
        public long? CoverId { get; set; }

        [JsonPropertyName("ratings_count")]
        public int? RatingsCount { get; set; }

        [JsonPropertyName("ratings_average")]
        public double? RatingsAverage { get; set; }

        /// <summary>Extracts the work OL ID from the key, e.g. "/works/OL45804W" -> "OL45804W"</summary>
        [JsonIgnore]
        public string WorkOlId => Key?.Split('/')[^1];

        /// <summary>First author OL ID from the author_key list, e.g. "/authors/OL23919A" -> "OL23919A"</summary>
        [JsonIgnore]
        public string PrimaryAuthorOlId => AuthorKey?.Count > 0 ? AuthorKey[0].Split('/')[^1] : null;
    }
}

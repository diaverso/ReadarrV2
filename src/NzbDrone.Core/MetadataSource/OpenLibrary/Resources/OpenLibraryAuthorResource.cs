using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    /// <summary>
    /// Maps to: GET https://openlibrary.org/authors/{OLID}.json
    /// Example: https://openlibrary.org/authors/OL23919A.json
    /// </summary>
    public class OpenLibraryAuthorResource
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("personal_name")]
        public string PersonalName { get; set; }

        [JsonPropertyName("bio")]
        [JsonConverter(typeof(PolymorphicStringConverter))]
        public string Bio { get; set; }

        [JsonPropertyName("birth_date")]
        public string BirthDate { get; set; }

        [JsonPropertyName("death_date")]
        public string DeathDate { get; set; }

        [JsonPropertyName("wikipedia")]
        public string Wikipedia { get; set; }

        [JsonPropertyName("photos")]
        public List<int> Photos { get; set; }

        /// <summary>Extracts the OL ID from the key path, e.g. "/authors/OL23919A" -> "OL23919A"</summary>
        [JsonIgnore]
        public string OlId => Key?.Split('/')[^1];
    }

    /// <summary>
    /// Maps to: GET https://openlibrary.org/search/authors.json?q={query}
    /// Individual result inside the "docs" array.
    /// </summary>
    public class OpenLibraryAuthorSearchDoc
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("birth_date")]
        public string BirthDate { get; set; }

        [JsonPropertyName("death_date")]
        public string DeathDate { get; set; }

        [JsonPropertyName("work_count")]
        public int? WorkCount { get; set; }

        [JsonPropertyName("top_subjects")]
        public List<string> TopSubjects { get; set; }

        // In author search results the key is just "OL23919A" (no /authors/ prefix)
        [JsonIgnore]
        public string OlId => Key?.Split('/')[^1];
    }

    public class OpenLibraryAuthorSearchResponse
    {
        [JsonPropertyName("numFound")]
        public int NumFound { get; set; }

        [JsonPropertyName("docs")]
        public List<OpenLibraryAuthorSearchDoc> Docs { get; set; }
    }

    /// <summary>
    /// Maps to: GET https://openlibrary.org/authors/{OLID}/works.json
    /// Entry inside the "entries" array.
    /// </summary>
    public class OpenLibraryAuthorWorksResponse
    {
        [JsonPropertyName("entries")]
        public List<OpenLibraryAuthorWorkEntry> Entries { get; set; }

        [JsonPropertyName("links")]
        public OpenLibraryPaginationLinks Links { get; set; }
    }

    public class OpenLibraryAuthorWorkEntry
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("covers")]
        public List<int> Covers { get; set; }

        [JsonPropertyName("first_publish_date")]
        public string FirstPublishDate { get; set; }

        [JsonIgnore]
        public string OlId => Key?.Split('/')[^1];
    }

    public class OpenLibraryPaginationLinks
    {
        [JsonPropertyName("next")]
        public string Next { get; set; }
    }
}

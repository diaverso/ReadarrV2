using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    /// <summary>
    /// Maps to: GET https://openlibrary.org/works/{OLID}/editions.json
    /// Example: https://openlibrary.org/works/OL45804W/editions.json?limit=50
    /// </summary>
    public class OpenLibraryEditionsResponse
    {
        [JsonPropertyName("entries")]
        public List<OpenLibraryEditionResource> Entries { get; set; }

        [JsonPropertyName("links")]
        public OpenLibraryEditionLinks Links { get; set; }
    }

    public class OpenLibraryEditionLinks
    {
        [JsonPropertyName("next")]
        public string Next { get; set; }
    }

    /// <summary>
    /// A single book edition from Open Library.
    /// Maps to individual entries inside editions.json or to GET /books/{OLID}.json
    /// </summary>
    public class OpenLibraryEditionResource
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("isbn_13")]
        public List<string> Isbn13 { get; set; }

        [JsonPropertyName("isbn_10")]
        public List<string> Isbn10 { get; set; }

        [JsonPropertyName("publishers")]
        public List<string> Publishers { get; set; }

        [JsonPropertyName("publish_date")]
        public string PublishDate { get; set; }

        [JsonPropertyName("number_of_pages")]
        public int? NumberOfPages { get; set; }

        [JsonPropertyName("physical_format")]
        public string PhysicalFormat { get; set; }

        [JsonPropertyName("covers")]
        public List<int> Covers { get; set; }

        [JsonPropertyName("languages")]
        public List<OpenLibraryLanguageRef> Languages { get; set; }

        [JsonPropertyName("notes")]
        [JsonConverter(typeof(PolymorphicStringConverter))]
        public string Notes { get; set; }

        [JsonPropertyName("description")]
        [JsonConverter(typeof(PolymorphicStringConverter))]
        public string Description { get; set; }

        [JsonPropertyName("works")]
        public List<OpenLibraryKeyRef> Works { get; set; }

        [JsonIgnore]
        public string OlId => Key?.Split('/')[^1];
    }

    public class OpenLibraryLanguageRef
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        // "/languages/eng" -> "eng"
        [JsonIgnore]
        public string Code => Key?.Split('/')[^1];
    }
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    /// <summary>
    /// Maps to: GET https://openlibrary.org/works/{OLID}.json
    /// Example: https://openlibrary.org/works/OL45804W.json
    /// </summary>
    public class OpenLibraryWorkResource
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("description")]
        [JsonConverter(typeof(PolymorphicStringConverter))]
        public string Description { get; set; }

        [JsonPropertyName("subjects")]
        public List<string> Subjects { get; set; }

        [JsonPropertyName("authors")]
        public List<OpenLibraryWorkAuthorEntry> Authors { get; set; }

        [JsonPropertyName("covers")]
        public List<int> Covers { get; set; }

        [JsonPropertyName("first_publish_date")]
        public string FirstPublishDate { get; set; }

        [JsonIgnore]
        public string OlId => Key?.Split('/')[^1];
    }

    public class OpenLibraryWorkAuthorEntry
    {
        [JsonPropertyName("author")]
        public OpenLibraryKeyRef Author { get; set; }
    }

    public class OpenLibraryKeyRef
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonIgnore]
        public string OlId => Key?.Split('/')[^1];
    }
}

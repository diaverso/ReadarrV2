using System.Linq;
using System.Text;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.OrganizerTests.FileNameBuilderTests
{
    [TestFixture]
    public class TruncatedBookTitlesFixture : CoreTest<FileNameBuilder>
    {
        // Max filename bytes on most filesystems
        private const int MaxFileNameLength = 255;

        private BookFile _bookFile;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _namingConfig = NamingConfig.Default;
            _namingConfig.RenameBooks = true;
            _namingConfig.StandardBookFormat = "{Book Title}";

            Mocker.GetMock<INamingConfigService>()
                .Setup(c => c.GetConfig()).Returns(_namingConfig);

            _bookFile = new BookFile { Quality = new QualityModel(Quality.EPUB), ReleaseGroup = "ReadarrTest" };

            Mocker.GetMock<IQualityDefinitionService>()
                .Setup(v => v.Get(Moq.It.IsAny<Quality>()))
                .Returns<Quality>(v => Quality.DefaultQualityDefinitions.First(c => c.Quality == v));
        }

        private (Author, Edition) BuildInputs(string authorName, string bookTitle, string seriesName = "Test Series", string seriesNumber = "1")
        {
            var author = Builder<Author>
                .CreateNew()
                .With(s => s.Name = authorName)
                .Build();

            var series = Builder<Series>
                .CreateNew()
                .With(x => x.Title = seriesName)
                .Build();

            var seriesLink = Builder<SeriesBookLink>
                .CreateListOfSize(1)
                .All()
                .With(s => s.Position = seriesNumber)
                .With(s => s.Series = series)
                .BuildListOfNew();

            var book = Builder<Book>
                .CreateNew()
                .With(s => s.Title = bookTitle)
                .With(s => s.AuthorMetadata = author.Metadata.Value)
                .With(s => s.SeriesLinks = seriesLink)
                .Build();

            var edition = Builder<Edition>
                .CreateNew()
                .With(s => s.Title = book.Title)
                .With(s => s.Book = book)
                .Build();

            return (author, edition);
        }

        private static string LongString(int byteLength, char c = 'A')
        {
            var sb = new StringBuilder();
            while (Encoding.UTF8.GetByteCount(sb.ToString()) < byteLength)
            {
                sb.Append(c);
            }

            // Trim back if we overshot
            while (Encoding.UTF8.GetByteCount(sb.ToString()) > byteLength)
            {
                sb.Remove(sb.Length - 1, 1);
            }

            return sb.ToString();
        }

        [Test]
        public void should_not_truncate_filename_when_under_limit()
        {
            var title = LongString(MaxFileNameLength - 10);
            var (author, edition) = BuildInputs("Author", title);

            var result = Subject.BuildBookFileName(author, edition, _bookFile);

            Encoding.UTF8.GetByteCount(result).Should().BeLessOrEqualTo(MaxFileNameLength);
            result.Should().NotContain("...");
        }

        [Test]
        public void should_not_truncate_filename_when_exactly_at_limit()
        {
            var title = LongString(MaxFileNameLength);
            var (author, edition) = BuildInputs("Author", title);

            var result = Subject.BuildBookFileName(author, edition, _bookFile);

            Encoding.UTF8.GetByteCount(result).Should().BeLessOrEqualTo(MaxFileNameLength);
            result.Should().NotContain("...");
        }

        [Test]
        public void should_truncate_filename_when_over_limit()
        {
            var title = LongString(MaxFileNameLength + 50);
            var (author, edition) = BuildInputs("Author", title);

            var result = Subject.BuildBookFileName(author, edition, _bookFile);

            Encoding.UTF8.GetByteCount(result).Should().BeLessOrEqualTo(MaxFileNameLength);
            result.Should().EndWith("...");
        }

        [Test]
        public void should_truncate_based_on_whole_pattern_length()
        {
            _namingConfig.StandardBookFormat = "{Author Name} - {Book Title}";
            var authorName = LongString(50);
            var title = LongString(MaxFileNameLength);  // total would exceed limit
            var (author, edition) = BuildInputs(authorName, title);

            var result = Subject.BuildBookFileName(author, edition, _bookFile);

            Encoding.UTF8.GetByteCount(result).Should().BeLessOrEqualTo(MaxFileNameLength);
            result.Should().EndWith("...");
        }
    }
}

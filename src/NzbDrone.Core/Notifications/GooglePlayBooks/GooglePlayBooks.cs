using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Notifications.GooglePlayBooks
{
    public class GooglePlayBooks : NotificationBase<GooglePlayBooksSettings>
    {
        private readonly IGooglePlayBooksProxy _proxy;
        private readonly Logger _logger;

        public GooglePlayBooks(IGooglePlayBooksProxy proxy, Logger logger)
        {
            _proxy = proxy;
            _logger = logger;
        }

        public override string Name => "Google Play Books";
        public override string Link => "https://play.google.com/books";

        public override void OnReleaseImport(BookDownloadMessage message)
        {
            var authorName = message.Author?.Metadata?.Value?.Name;

            string seriesName = null;
            try
            {
                seriesName = message.Book?.SeriesLinks?.Value
                    ?.Find(x => x.IsPrimary)?.Series?.Value?.Title
                    ?? message.Book?.SeriesLinks?.Value
                        ?.Find(x => x.Series?.Value != null)?.Series?.Value?.Title;
            }
            catch
            {
                // SeriesLinks may not be loaded — upload without series folder
            }

            foreach (var bookFile in message.BookFiles)
            {
                try
                {
                    _proxy.UploadBook(bookFile.Path, authorName, seriesName, Settings);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to upload {0} to Google Play Books", bookFile.Path);
                }
            }
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            try
            {
                _proxy.TestConnection(Settings);
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure("RefreshToken", ex.Message));
            }

            return new ValidationResult(failures);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLog;
using NzbDrone.Core.MetadataSource.Goodreads;

namespace NzbDrone.Core.Books.Services
{
    public interface IVerifiedGoodreadsWorkService
    {
        Book Resolve(Book request, string workProviderId, string authorProviderId);
    }

    // Goodreads autocomplete is discovery only. Full metadata independently proves every
    // identity before a candidate can participate in the normal exact-work add pipeline.
    public class VerifiedGoodreadsWorkService : IVerifiedGoodreadsWorkService
    {
        private const int MaximumCandidates = 2;
        private readonly IGoodreadsProxy _goodreads;
        private readonly Logger _logger;
        private static readonly HashSet<string> PrintFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            "Paperback", "Hardcover", "Mass Market Paperback", "Library Binding", "Spiral-bound", "Board Book", "Pamphlet"
        };
        private static readonly HashSet<string> EbookFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            "Kindle Edition", "ebook", "Nook", "NOOK Book"
        };

        public VerifiedGoodreadsWorkService(IGoodreadsProxy goodreads, Logger logger)
        {
            _goodreads = goodreads;
            _logger = logger;
        }

        public Book Resolve(Book request, string workProviderId, string authorProviderId)
        {
            // Initial fallback supports ebook acquisitions only. A print representative does
            // not establish audiobook identity, narration or duration.
            if (request?.MediaType != BookMediaType.Ebook || CanonicalId(workProviderId) != workProviderId ||
                CanonicalId(authorProviderId) != authorProviderId) return null;

            var candidates = (request.Editions ?? new List<Edition>())
                .OrderByDescending(x => x.Monitored)
                .SelectMany(BookEditionIdentity.GetEditionProviderIds)
                .Where(x => x?.StartsWith("gr:", StringComparison.OrdinalIgnoreCase) == true)
                .Select(CanonicalId).Where(x => x != null)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumCandidates).ToList();
            foreach (var candidate in candidates)
            {
                try
                {
                    // Numeric edition path in the existing fixed-host client; no client URL is accepted.
                    var book = _goodreads.GetBookInfo(candidate.Substring(3), useCache: true);
                    if (CanonicalId(book?.GoodreadsWorkId) != workProviderId ||
                        CanonicalId(book?.GoodreadsBookId) != candidate ||
                        CanonicalId(book?.Author?.GoodreadsAuthorId) != authorProviderId ||
                        book.Editions?.Count != 1 || string.IsNullOrWhiteSpace(book.Title) || string.IsNullOrWhiteSpace(book.Author.Name))
                    {
                        _logger.Debug("Independent Goodreads candidate {0} did not verify requested work/author/edition identity; retaining pending request", candidate);
                        continue;
                    }
                    var edition = book.Editions[0];
                    if (CanonicalId(edition.ForeignEditionId) != candidate || string.IsNullOrWhiteSpace(edition.Language) || string.IsNullOrWhiteSpace(edition.Title))
                    {
                        _logger.Debug("Independent Goodreads candidate {0} has incompatible edition identity or missing language; retaining pending request", candidate);
                        continue;
                    }
                    var format = edition.Format?.Trim();
                    if (edition.IsEbook && EbookFormats.Contains(format ?? string.Empty)) edition.ReadingFormatId = 3;
                    else if (!edition.IsEbook && PrintFormats.Contains(format ?? string.Empty)) edition.ReadingFormatId = 1;
                    else
                    {
                        _logger.Debug("Independent Goodreads candidate {0} has unverified format; retaining pending request", candidate);
                        continue;
                    }

                    book.GoodreadsWorkId = workProviderId;
                    book.GoodreadsBookId = candidate;
                    book.Author.GoodreadsAuthorId = authorProviderId;
                    book.MediaType = request.MediaType;
                    edition.GoodreadsEditionId = long.Parse(candidate.Substring(3), CultureInfo.InvariantCulture);
                    edition.ForeignEditionId = candidate + "-ebook";
                    edition.Book = book;
                    _logger.Info("Verified independent Goodreads metadata for requested work {0}, edition {1}; source format {2}", workProviderId, candidate, format);
                    return book;
                }
                catch (Exception error)
                {
                    // Fallback failures never replace the original temporary metadata outcome.
                    _logger.Debug("Independent Goodreads candidate {0} unavailable ({1}); retaining pending request", candidate, error.GetType().Name);
                }
            }
            return null;
        }

        private static string CanonicalId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var raw = value.Trim();
            if (raw.StartsWith("gr:", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(3);
            return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                ? "gr:" + id.ToString(CultureInfo.InvariantCulture) : null;
        }
    }
}

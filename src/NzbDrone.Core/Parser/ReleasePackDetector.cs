using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Parser
{
    public enum ReleasePackDetectionVerdict
    {
        None = 0,
        SingleBookSplitRelease = 1,
        MultipleBooks = 2,
        AudiobookFragment = 3
    }

    public sealed class ReleasePackDetection
    {
        public ReleasePackDetectionVerdict Verdict { get; set; }
        public string PackType { get; set; }
        public string MatchedValue { get; set; }

        public static ReleasePackDetection None()
        {
            return new ReleasePackDetection { Verdict = ReleasePackDetectionVerdict.None };
        }
    }

    public static class ReleasePackDetector
    {
        private static readonly Regex BookRangeRegex = new Regex(@"\b(?:series\s*[-:]?\s*)?(?:books?|bks?|vol(?:ume)?s?)\s*#?\d{1,3}\s*(?:[-–—]|to|through|thru)\s*#?\d{1,3}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex BookAndRegex = new Regex(@"\b(?:books?|bks?)\s*#?\d{1,3}\s*(?:and|&)\s*#?\d{1,3}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex BracketRangeRegex = new Regex(@"\[[^\]]*\b\d{1,3}\s*[-–—]\s*\d{1,3}\b[^\]]*\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex NamedPackRegex = new Regex(@"\b(?:trilogy|tetralogy|quadrilogy|pentalogy|quintilogy|hexalogy|heptalogy|octology|omnibus|discography|discografia)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CompletePackRegex = new Regex(@"\b(?:complete|full)\b.{0,80}\b(?:series|collection|saga|set|works|anthology|trilogy|tetralogy)\b|\b(?:box(?:ed)?\s+set|audio\s+collection|series\s+collection)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SingleBookSplitRegex = new Regex(@"\(\s*\d{1,3}\s+of\s+\d{1,3}\s*\)|\b(?:part|pt)\s*#?\d{1,3}(?:\s*(?:of|/)\s*\d{1,3})?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AudiobookFragmentRegex = new Regex(@"\b(?:cd|disc|disk|track|trk)\s*#?\d{1,3}(?:\s*(?:of|/)\s*\d{1,3})?\b(?!\s*(?:k|kbps|mb|gb)\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static ReleasePackDetection Detect(string releaseTitle, Book targetBook, IEnumerable<Book> authorCatalog)
        {
            if (string.IsNullOrWhiteSpace(releaseTitle))
            {
                return ReleasePackDetection.None();
            }

            var explicitPack = DetectExplicitMultiBookPack(releaseTitle, targetBook);
            if (explicitPack != null)
            {
                return explicitPack;
            }

            var splitMatch = SingleBookSplitRegex.Match(releaseTitle);
            if (splitMatch.Success)
            {
                return new ReleasePackDetection
                {
                    Verdict = ReleasePackDetectionVerdict.SingleBookSplitRelease,
                    PackType = "single-book-split",
                    MatchedValue = splitMatch.Value
                };
            }

            var fragmentMatch = AudiobookFragmentRegex.Match(releaseTitle);
            if (fragmentMatch.Success)
            {
                return new ReleasePackDetection
                {
                    Verdict = ReleasePackDetectionVerdict.AudiobookFragment,
                    PackType = "audiobook-fragment",
                    MatchedValue = fragmentMatch.Value
                };
            }

            return ReleasePackDetection.None();
        }

        private static bool IsRequestedOmnibusMarker(string releaseTitle, Match marker, Book targetBook)
        {
            if (targetBook?.IsOmnibus != true ||
                !string.Equals(marker.Value, "omnibus", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(targetBook.Author?.Name) ||
                !ReleaseTitleMatchScorer.Tokenize(ReleaseTitleMatchScorer.GetPrimaryBookTitle(targetBook)).Contains("omnibus"))
            {
                return false;
            }

            // VIP is an indexer access tag, not a book title. Keep this removal
            // limited to that complete bracketed tag; pack checks use the original.
            var identityTitle = Regex.Replace(Parser.CleanReleaseTitleForParsing(releaseTitle),
                @"\[\s*VIP\s*\]", string.Empty, RegexOptions.IgnoreCase);
            var match = ReleaseTitleMatchScorer.FindBestMatch(identityTitle,
                                                              targetBook.Author.Name,
                                                              new[] { targetBook });
            if (match?.IsMatch != true || match.MeaningfulLeftoverCount != 0)
            {
                return false;
            }

            // Match only this work, its author, and existing metadata tokens.
            // Unknown extra titles cannot hide behind an omnibus exemption,
            // and this needs no author-catalog traversal for each release.
            var authorTokens = ReleaseTitleMatchScorer.Tokenize(targetBook.Author.Name);
            return ReleaseTitleMatchScorer.Tokenize(identityTitle)
                .Select((token, index) => (token, index))
                .All(item => (item.index >= match.MatchedStart && item.index <= match.MatchedEnd) ||
                             authorTokens.Contains(item.token) || ReleaseTitleMatchScorer.IsMetadataToken(item.token));
        }

        private static ReleasePackDetection DetectExplicitMultiBookPack(string releaseTitle, Book targetBook)
        {
            foreach (var (regex, packType) in new[]
            {
                (BookRangeRegex, "book-range"),
                (BookAndRegex, "book-list"),
                (BracketRangeRegex, "bracketed-range"),
                (NamedPackRegex, "named-pack"),
                (CompletePackRegex, "complete-pack")
            })
            {
                foreach (Match match in regex.Matches(releaseTitle))
                {
                    // An omnibus can itself be the one explicitly requested
                    // work. Exempt only its intrinsic title marker, never other
                    // pack/range markers or unrelated works bundled with it.
                    if (regex == NamedPackRegex &&
                        IsRequestedOmnibusMarker(releaseTitle, match, targetBook))
                    {
                        continue;
                    }

                    return new ReleasePackDetection
                    {
                        Verdict = ReleasePackDetectionVerdict.MultipleBooks,
                        PackType = packType,
                        MatchedValue = match.Value
                    };
                }
            }

            return null;
        }
    }
}

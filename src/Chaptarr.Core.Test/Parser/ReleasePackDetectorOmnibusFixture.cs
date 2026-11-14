using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace Chaptarr.Core.Test.Parser
{
    [TestFixture]
    public class ReleasePackDetectorOmnibusFixture
    {
        [TestCase("Wool Omnibus Edition by Hugh Howey [ENG / M4B] [VIP]")]
        [TestCase("Hugh Howey - Wool Omnibus Edition [M4B]")]
        [TestCase("Wool Omnibus Edition by Hugh Howey [128-320kbps] [M4B]")]
        public void should_accept_the_requested_omnibus_work(string release)
        {
            var target = BuildTarget();
            var result = ReleasePackDetector.Detect(release, target);

            Assert.That(result.Verdict, Is.EqualTo(ReleasePackDetectionVerdict.None),
                $"Type={result.PackType}; Match={result.MatchedValue}");
        }

        [Test]
        public void should_use_the_selected_omnibus_edition_title_over_the_canonical_work_title()
        {
            var target = BuildTarget();
            target.Title = "Wool";
            target.Editions = new List<Edition>
            {
                new Edition { Id = 113891, Title = "Wool Omnibus Edition (Wool 1 - 5)", Monitored = true }
            };
            var result = ReleasePackDetector.Detect("Wool Omnibus Edition by Hugh Howey [ENG / M4B] [VIP]", target);

            Assert.That(result.Verdict, Is.EqualTo(ReleasePackDetectionVerdict.None));
        }

        [TestCase("Wool Shift Omnibus by Hugh Howey [M4B]")]
        [TestCase("Wool Omnibus Edition Shift by Hugh Howey [M4B]")]
        [TestCase("Shift Omnibus Edition by Hugh Howey [M4B]")]
        [TestCase("Wool Omnibus Edition by Stephen King [M4B]")]
        [TestCase("Wool Omnibus Edition and Shift by Hugh Howey [M4B]")]
        [TestCase("Wool Omnibus Edition by Hugh Howey Books 1-3 [M4B]")]
        [TestCase("Wool Omnibus Edition by Hugh Howey Books 1 and 2 [M4B]")]
        [TestCase("Wool Omnibus Edition by Hugh Howey [1-5] [M4B]")]
        [TestCase("Wool Omnibus Edition by Hugh Howey Complete Collection [M4B]")]
        [TestCase("Wool Omnibus Edition by Hugh Howey Trilogy [M4B]")]
        [TestCase("Wool Omnibus Edition by Hugh Howey Omnibus [M4B]")]
        public void should_keep_rejecting_other_works_and_additional_pack_markers(string release)
        {
            var target = BuildTarget();
            var result = ReleasePackDetector.Detect(release, target);

            Assert.That(result.Verdict, Is.EqualTo(ReleasePackDetectionVerdict.MultipleBooks));
        }

        [TestCase(false, "Wool Omnibus Edition (Wool 1 - 5)")]
        [TestCase(true, "Wool")]
        [TestCase(true, "Wool Omnibus Edition (Shift 1 - 5)")]
        [TestCase(true, "Wool Omnibus Edition (Special Archive)")]
        [TestCase(true, "Wool Omnibus Edition (Wool 5 - 1)")]
        public void should_require_explicit_omnibus_metadata_and_an_intrinsic_title_marker(bool isOmnibus, string title)
        {
            var target = BuildTarget();
            target.IsOmnibus = isOmnibus;
            target.Title = title;
            var result = ReleasePackDetector.Detect("Wool Omnibus Edition by Hugh Howey [M4B]", target);

            Assert.That(result.Verdict, Is.EqualTo(ReleasePackDetectionVerdict.MultipleBooks));
        }

        [Test]
        public void should_not_exempt_omnibus_without_a_requested_target_or_known_author()
        {
            var target = BuildTarget();
            Assert.That(ReleasePackDetector.Detect("Wool Omnibus Edition by Hugh Howey [M4B]", null).Verdict,
                Is.EqualTo(ReleasePackDetectionVerdict.MultipleBooks));
            target.Author = null;
            Assert.That(ReleasePackDetector.Detect("Wool Omnibus Edition by Hugh Howey [M4B]", target).Verdict,
                Is.EqualTo(ReleasePackDetectionVerdict.MultipleBooks));
        }

        [Test]
        public void should_keep_structured_ranges_covering_additional_works_rejected()
        {
            var target = BuildTarget();
            target.SeriesName = "Wool";
            target.SeriesPosition = "1-5";
            var release = new ReleaseInfo
            {
                Title = "Wool Omnibus Edition by Hugh Howey [M4B]",
                SeriesInfo = new List<ReleaseSeriesInfo>
                {
                    new ReleaseSeriesInfo { Name = "Wool", Position = "1-6" }
                }
            };

            var result = ReleasePackDetector.Detect(release, target);

            Assert.That(result.Verdict, Is.EqualTo(ReleasePackDetectionVerdict.MultipleBooks));
            Assert.That(result.PackType, Is.EqualTo("structured-series-range"));
        }

        private static Book BuildTarget()
        {
            return new Book
            {
                Id = 46503,
                Title = "Wool Omnibus Edition (Wool 1 - 5)",
                IsOmnibus = true,
                MediaType = BookMediaType.Audiobook,
                Author = new Author { Id = 102, Name = "Hugh Howey" }
            };
        }

    }
}

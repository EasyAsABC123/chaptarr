using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Services;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource.Goodreads;

namespace Chaptarr.Core.Test.Books
{
    [TestFixture]
    public class VerifiedGoodreadsWorkFixture
    {
        public class CacheProxy : DispatchProxy
        {
            public Func<HttpRequest, HttpResponse> Get;
            protected override object Invoke(MethodInfo method, object[] args) => Get((HttpRequest)args[0]);
        }

        internal static VerifiedGoodreadsWorkService Create(string work = "42", string edition = "77", string author = "7",
            string format = "Paperback", bool ebook = false, string language = "eng", HttpStatusCode status = HttpStatusCode.OK,
            Action<HttpRequest> observed = null)
        {
            var cache = DispatchProxy.Create<ICachedHttpResponseService, CacheProxy>();
            ((CacheProxy)(object)cache).Get = request =>
            {
                observed?.Invoke(request);
                var isbn = work == "98976523" ? "9781680513301" : "9781594858314";
                var xml = $@"<GoodreadsResponse><book><id>{edition}</id><title>Verified exact work</title><title_without_series>Verified exact work</title_without_series>
                    <isbn13>{isbn}</isbn13><language_code>{language}</language_code><format>{format}</format><is_ebook>{ebook.ToString().ToLowerInvariant()}</is_ebook>
                    <num_pages>304</num_pages><average_rating>4</average_rating><ratings_count>10</ratings_count><text_reviews_count>1</text_reviews_count>
                    <work><id>{work}</id><original_title>Verified exact work</original_title><books_count>1</books_count><reviews_count>10</reviews_count><ratings_count>10</ratings_count><average_rating>4</average_rating><text_reviews_count>1</text_reviews_count></work>
                    <authors><author><id>{author}</id><name>Verified Author</name></author></authors>
                    </book></GoodreadsResponse>";
                return new HttpResponse(request, new HttpHeader { ContentType = "application/xml" }, xml, status);
            };
            var logger = LogManager.GetCurrentClassLogger();
            return new VerifiedGoodreadsWorkService(new GoodreadsProxy(cache, logger), logger);
        }

        private static Book Request() => new Book { MediaType = BookMediaType.Ebook,
            Editions = new List<Edition> { new Edition { ForeignEditionId = "gr:77", Monitored = true } } };

        [TestCase("47168622", "27130746", "14694702")]
        [TestCase("98976523", "63884705", "49899833")]
        public void independent_full_metadata_verifies_exact_identity_and_preserves_print_classification(string work, string edition, string author)
        {
            var request = Request(); request.Editions[0].ForeignEditionId = "gr:" + edition;
            var calls = 0;
            var result = Create(work, edition, author, observed: r => {
                calls++; Assert.That(r.Url.Host, Is.EqualTo("www.goodreads.com"));
                Assert.That(r.Url.FullUri, Does.Contain("/api/book/basic_book_data/" + edition));
            }).Resolve(request, "gr:" + work, "gr:" + author);
            Assert.That(calls, Is.EqualTo(1)); Assert.That(result, Is.Not.Null);
            Assert.That(result.GoodreadsWorkId, Is.EqualTo("gr:" + work));
            Assert.That(result.Author.GoodreadsAuthorId, Is.EqualTo("gr:" + author));
            Assert.That(result.Editions[0].GoodreadsEditionId.ToString(), Is.EqualTo(edition));
            Assert.That(result.Editions[0].ReadingFormatId, Is.EqualTo(1));
            Assert.That(result.Editions[0].IsEbook, Is.False); Assert.That(result.Editions[0].Format, Is.EqualTo("Paperback"));
            Assert.That(result.Editions[0].Language, Is.EqualTo("eng")); Assert.That(result.MediaType, Is.EqualTo(BookMediaType.Ebook));
        }

        [TestCase("43", "77", "7", "Paperback", false, "eng", HttpStatusCode.OK)]
        [TestCase("42", "78", "7", "Paperback", false, "eng", HttpStatusCode.OK)]
        [TestCase("42", "77", "8", "Paperback", false, "eng", HttpStatusCode.OK)]
        [TestCase("42", "77", "7", "Unknown", false, "eng", HttpStatusCode.OK)]
        [TestCase("42", "77", "7", "Paperback", true, "eng", HttpStatusCode.OK)]
        [TestCase("42", "77", "7", "Paperback", false, "", HttpStatusCode.OK)]
        [TestCase("42", "77", "7", "Paperback", false, "eng", HttpStatusCode.ServiceUnavailable)]
        public void mismatched_unknown_or_unavailable_metadata_is_rejected(string work, string edition, string author, string format, bool ebook, string language, HttpStatusCode status)
        {
            Assert.That(Create(work, edition, author, format, ebook, language, status).Resolve(Request(), "gr:42", "gr:7"), Is.Null);
        }

        [Test]
        public void candidates_are_bounded_and_urls_or_audiobook_requests_never_become_provider_paths()
        {
            var request = Request(); request.Editions.Add(new Edition { ForeignEditionId = "gr:78" }); request.Editions.Add(new Edition { ForeignEditionId = "gr:79" });
            request.Editions.Add(new Edition { ForeignEditionId = "https://attacker.invalid/80" });
            var calls = 0; var service = Create(work: "43", observed: _ => calls++);
            Assert.That(service.Resolve(request, "gr:42", "gr:7"), Is.Null); Assert.That(calls, Is.EqualTo(2));
            request.MediaType = BookMediaType.Audiobook;
            Assert.That(service.Resolve(request, "gr:42", "gr:7"), Is.Null); Assert.That(calls, Is.EqualTo(2));
        }
    }
}

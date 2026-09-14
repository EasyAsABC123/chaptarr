using System.Linq;
using System.Text.Json;
using Chaptarr.Api.V1.Series;
using NUnit.Framework;

namespace Chaptarr.Core.Test.Api.Series
{
    [TestFixture]
    public class SeriesBookIdentifiersFixture
    {
        [TestCase(false)]
        [TestCase(true)]
        public void identifier_query_is_opt_in_and_serializes_book_evidence(bool includeIdentifiers)
        {
            var http = SeriesMonitorFixture.Proxy<NzbDrone.Common.Http.IHttpClient>((method, args) =>
            {
                var request = (NzbDrone.Common.Http.HttpRequest)args[0];
                var query = System.Text.Encoding.UTF8.GetString(request.ContentData);
                Assert.That(query.Contains("editions(order_by"), Is.EqualTo(includeIdentifiers));
                var source = "{\"data\":{\"series_by_pk\":{\"id\":1,\"name\":\"Series\",\"primary_books_count\":1,\"book_series\":[{\"position\":1,\"book\":{\"id\":2,\"title\":\"Book\",\"editions\":[{\"isbn_13\":\"9780140328721\"}]}}]}}}";
                return new NzbDrone.Common.Http.HttpResponse(request, new NzbDrone.Common.Http.HttpHeader(), source);
            });
            var config = SeriesMonitorFixture.Proxy<NzbDrone.Core.Configuration.IConfigService>((method, args) => method == "get_HardcoverEnabled" ? true : "fixture-token");
            var controller = new SeriesLookupController(http, config, null, null, NLog.LogManager.GetCurrentClassLogger());
            var response = controller.GetSeriesInfo("hc:1", includeBookIdentifiers: includeIdentifiers);
            var value = (SeriesLookupResource)((Microsoft.AspNetCore.Mvc.OkObjectResult)response.Result).Value;
            Assert.That(value.Books.Single().IdentifiersComplete, Is.EqualTo(includeIdentifiers ? (bool?)true : null));
            if (includeIdentifiers) Assert.That(value.Books.Single().Isbns, Is.EqualTo(new[] { "9780140328721" }));
        }

        [Test]
        public void reads_exact_book_editions_and_deduplicates_identifiers()
        {
            using var json = JsonDocument.Parse("{\"editions\":[{\"isbn_13\":\"9780140328721\",\"isbn_10\":\"0140328726\"},{\"isbn_13\":\"9780140328721\"}],\"otherBook\":{\"isbn_13\":\"wrong\"}}");
            var result = SeriesBookIdentifiers.Read(json.RootElement);
            Assert.That(result.Complete, Is.True);
            Assert.That(result.Isbns, Is.EquivalentTo(new[] { "9780140328721", "0140328726" }));
        }

        [TestCase("{}", false)]
        [TestCase("{\"editions\":null}", false)]
        [TestCase("{\"editions\":[null]}", false)]
        [TestCase("{\"editions\":[{\"isbn_13\":123}]}", false)]
        [TestCase("{\"editions\":[]}", true)]
        public void distinguishes_absent_malformed_and_empty_evidence(string source, bool complete)
        {
            using var json = JsonDocument.Parse(source);
            Assert.That(SeriesBookIdentifiers.Read(json.RootElement).Complete, Is.EqualTo(complete));
        }

        [Test]
        public void sentinel_edition_marks_incomplete_evidence()
        {
            var source = JsonSerializer.Serialize(new { editions = Enumerable.Range(0, 201).Select(id => new { isbn_13 = id.ToString() }) });
            using var json = JsonDocument.Parse(source);
            var result = SeriesBookIdentifiers.Read(json.RootElement);
            Assert.That(result.Complete, Is.False);
            Assert.That(result.Isbns.Count, Is.EqualTo(200));
        }

        [Test]
        public void default_series_resources_omit_identifier_extension()
        {
            var json = JsonSerializer.Serialize(new SeriesBookResource { ForeignBookId = "hc:1", Title = "Book" });
            Assert.That(json, Does.Not.Contain("isbns"));
            Assert.That(json, Does.Not.Contain("identifiersComplete"));
        }
    }
}

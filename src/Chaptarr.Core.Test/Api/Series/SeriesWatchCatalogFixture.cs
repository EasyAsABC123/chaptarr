using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.SeriesWatch;
using NUnit.Framework;
using static Chaptarr.Core.Test.Api.Series.SeriesMonitorFixture;

namespace Chaptarr.Core.Test.Api.Series
{
    [TestFixture]
    public class SeriesWatchCatalogFixture
    {
        [TestCase(false)]
        [TestCase(true)]
        public void fetches_all_pages_and_sees_later_catalog_additions(bool includeExtras)
        {
            var total = 101;
            var calls = 0;
            var http = Proxy<IHttpClient>((method, args) =>
            {
                calls++;
                var request = (HttpRequest)args[0];
                using var body = JsonDocument.Parse(Encoding.UTF8.GetString(request.ContentData));
                Assert.That(body.RootElement.GetProperty("query").GetString().Contains("featured"), Is.EqualTo(!includeExtras));
                Assert.That(body.RootElement.GetProperty("variables").GetProperty("id").GetInt32(), Is.EqualTo(988));
                var offset = body.RootElement.GetProperty("variables").GetProperty("offset").GetInt32();
                var rows = Enumerable.Range(offset + 1, Math.Min(100, total - offset)).Select(id => new
                {
                    book = new { id, title = $"Book {id}", contributions = new[] { new { author = new { id = 50, name = "Author" } } } }
                });
                return new HttpResponse(request, new HttpHeader(), JsonSerializer.Serialize(new { data = new { series_by_pk = new { id = 988, book_series = rows } } }));
            });
            var config = Proxy<IConfigService>((method, args) => method == "get_HardcoverEnabled" ? true : "fixture-token");
            var catalog = new SeriesWatchCatalog(http, config, null);
            var first = catalog.Fetch("hc:988", includeExtras);
            Assert.That(first.Count, Is.EqualTo(101)); Assert.That(calls, Is.EqualTo(2));
            Assert.That(first[0].BookProviderId, Is.EqualTo("hc:1"));
            Assert.That(first[0].AuthorProviderId, Is.EqualTo("hc:50"));
            total = 102;
            Assert.That(catalog.Fetch("hc:988", includeExtras).Last().BookProviderId, Is.EqualTo("hc:102"));
        }
        [TestCase("{\"errors\":[{\"message\":\"denied\"}],\"data\":null}")]
        [TestCase("{\"data\":{\"series_by_pk\":null}}")]
        [TestCase("{\"data\":{\"series_by_pk\":{\"id\":999,\"book_series\":[]}}}")]
        public void rejects_failed_or_wrong_identity_responses(string source)
        {
            var http = Proxy<IHttpClient>((method, args) => new HttpResponse((HttpRequest)args[0], new HttpHeader(), source));
            var config = Proxy<IConfigService>((method, args) => method == "get_HardcoverEnabled" ? true : "fixture-token");
            Assert.Throws<InvalidOperationException>(() => new SeriesWatchCatalog(http, config, null).Fetch("hc:988", false));
        }
    }
}

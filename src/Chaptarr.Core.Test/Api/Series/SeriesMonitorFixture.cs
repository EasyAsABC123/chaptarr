using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Chaptarr.Api.V1.Series;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace Chaptarr.Core.Test.Api.Series
{
    [TestFixture]
    public class SeriesMonitorFixture
    {
        private NzbDrone.Core.Books.Series _series;
        private List<Book> _books;
        private int _writes;
        private bool _blocked;
        private SeriesMonitorController _subject;

        public static T Proxy<T>(Func<string, object[], object> handler)
            where T : class
        {
            var proxy = DispatchProxy.Create<T, ServiceProxy>();
            ((ServiceProxy)(object)proxy).Handler = handler;
            return proxy;
        }

        public class ServiceProxy : DispatchProxy
        {
            public Func<string, object[], object> Handler { get; set; }
            protected override object Invoke(MethodInfo method, object[] args) => Handler(method.Name, args);
        }

        [SetUp]
        public void SetUp()
        {
            _series = new NzbDrone.Core.Books.Series { Id = 1, MediaType = BookMediaType.Ebook };
            _books = new List<Book> { new Book { Id = 10, MediaType = BookMediaType.Ebook } };
            _writes = 0;
            _blocked = false;
            _subject = new SeriesMonitorController(
                Proxy<ISeriesService>((method, args) => method == nameof(ISeriesService.GetSeries) ? _series : throw new NotImplementedException(method)),
                Proxy<ISeriesBookLinkService>((method, args) => method == nameof(ISeriesBookLinkService.GetLinksBySeries)
                    ? new List<SeriesBookLink> { new SeriesBookLink { SeriesId = 1, BookId = 10 } } : throw new NotImplementedException(method)),
                Proxy<IBookService>((method, args) =>
                {
                    if (method == nameof(IBookService.GetExistingBooks))
                    {
                        return _books;
                    }

                    if (method == nameof(IBookService.SetMonitoredForMediaType))
                    {
                        _writes++;
                        Assert.That(args[0], Is.EqualTo(new[] { 10 }));
                        Assert.That(args[1], Is.EqualTo(_series.MediaType == BookMediaType.Ebook ? "ebook" : "audiobook"));
                        if (!_blocked)
                        {
                            _books[0].SetMonitoredForMediaType((string)args[1], (bool)args[2]);
                        }

                        return null;
                    }

                    throw new NotImplementedException(method);
                }));
        }

        [TestCase(BookMediaType.Ebook, true)]
        [TestCase(BookMediaType.Ebook, false)]
        [TestCase(BookMediaType.Audiobook, true)]
        [TestCase(BookMediaType.Audiobook, false)]
        public void scopes_and_deduplicates_monitor_changes(BookMediaType format, bool monitored)
        {
            _series.MediaType = format;
            _books[0].MediaType = format;
            var request = new SeriesMonitorResource { BookIds = new[] { 10, 10 }, Monitored = monitored };
            Assert.That(_subject.Monitor(1, request), Is.TypeOf<OkObjectResult>());
            Assert.That(_subject.Monitor(1, request), Is.TypeOf<OkObjectResult>(), "Retry is idempotent");
            Assert.That(_writes, Is.EqualTo(2));
        }

        [Test]
        public void rejects_nonmembers_before_any_mutation()
        {
            Assert.That(_subject.Monitor(1, new SeriesMonitorResource { BookIds = new[] { 10, 20 }, Monitored = true }), Is.TypeOf<BadRequestObjectResult>());
            Assert.That(_writes, Is.Zero);
        }

        [Test]
        public void rejects_cross_format_members_before_any_mutation()
        {
            _books[0].MediaType = BookMediaType.Audiobook;
            Assert.That(_subject.Monitor(1, new SeriesMonitorResource { BookIds = new[] { 10 }, Monitored = true }), Is.TypeOf<ConflictObjectResult>());
            Assert.That(_writes, Is.Zero);
        }

        [Test]
        public void reports_author_policy_refusal_instead_of_false_success()
        {
            _blocked = true;
            Assert.That(_subject.Monitor(1, new SeriesMonitorResource { BookIds = new[] { 10 }, Monitored = true }), Is.TypeOf<ConflictObjectResult>());
        }

        [Test]
        public void rejects_deleted_members_and_series()
        {
            _books.Clear();
            Assert.That(_subject.Monitor(1, new SeriesMonitorResource { BookIds = new[] { 10 }, Monitored = true }), Is.TypeOf<ConflictObjectResult>());
            _series = null;
            Assert.That(_subject.Monitor(1, new SeriesMonitorResource { BookIds = new[] { 10 }, Monitored = true }), Is.TypeOf<NotFoundResult>());
            Assert.That(_writes, Is.Zero);
        }

        [Test]
        public void validates_required_fields_and_size_before_writing()
        {
            foreach (var request in new[]
            {
                null,
                new SeriesMonitorResource { BookIds = new[] { 10 } },
                new SeriesMonitorResource { Monitored = true },
                new SeriesMonitorResource { BookIds = Array.Empty<int>(), Monitored = true },
                new SeriesMonitorResource { BookIds = new[] { -1 }, Monitored = true },
                new SeriesMonitorResource { BookIds = Enumerable.Range(1, 501).ToArray(), Monitored = true }
            })
            {
                Assert.That(_subject.Monitor(1, request), Is.TypeOf<BadRequestObjectResult>());
            }

            Assert.That(_writes, Is.Zero);
        }
    }
}

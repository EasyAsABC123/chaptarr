using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Books.Services;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Profiles.Metadata;

namespace Chaptarr.Core.Test.Books
{
    [TestFixture]
    public class DurableSingleWorkAdmissionFixture
    {
        public class Proxy<T> : DispatchProxy where T : class
        {
            public Func<MethodInfo, object[], object> Handler;
            protected override object Invoke(MethodInfo method, object[] args) => Handler(method, args);
        }
        private static T Stub<T>(Func<MethodInfo, object[], object> handler = null) where T : class
        {
            var proxy = DispatchProxy.Create<T, Proxy<T>>();
            ((Proxy<T>)(object)proxy).Handler = handler ?? ((m, a) => throw new AssertionException("Unexpected call: " + typeof(T).Name + "." + m.Name));
            return proxy;
        }
        private sealed class Harness
        {
            public readonly List<PendingAuthorImport> Rows = new();
            public readonly List<Book> Books = new();
            public readonly List<int> Searches = new();
            public readonly List<object> Events = new();
            public Exception Failure = new BookMetadataPendingException("gr:42");
            public Action DuringFetch;
            public bool FailSearch;
            public AddBookService Add;
            public PendingAuthorImportService Pending;
            public ProcessPendingImportsCommandHandler Worker;
            public readonly Author Author = new Author { Name = "Requested Author", GoodreadsAuthorId = "gr:7", EbookMetadataProfileId = 1, AudiobookMetadataProfileId = 1, EbookMonitored = true, AudiobookMonitored = true };
            private readonly IPendingAuthorImportRepository _repository;
            private readonly IAuthorService _authors;
            private readonly IBookService _books;
            private readonly IEventAggregator _events;
            private readonly IProvideBookInfo _info;
            public Harness()
            {
                _repository = Stub<IPendingAuthorImportRepository>((m, a) =>
                {
                    if (m.Name == "GetActiveByRequestKey") return Clone(Rows.FirstOrDefault(x => x.RequestKey == (string)a[0] && x.IsActive()));
                    if (m.Name == "GetActiveByProviderId") return Clone(Rows.FirstOrDefault(x => !x.SingleWorkOnly && x.ProviderId == (string)a[0] && x.IsActive()));
                    if (m.Name == "Find") return Clone(Rows.FirstOrDefault(x => x.Id == (int)a[0]));
                    if (m.Name == "Insert") { var row = (PendingAuthorImport)a[0]; row.Id = Rows.Count + 1; Rows.Add(Clone(row)); return row; }
                    if (m.Name == "TryUpdateRequest") { var row = (PendingAuthorImport)a[0]; var old = Rows.FirstOrDefault(x => x.Id == row.Id); if (old?.Version != (long)a[1]) return false; row.Version++; Rows[Rows.IndexOf(old)] = Clone(row); return true; }
                    if (m.Name == "TryDelete") { var row = Rows.FirstOrDefault(x => x.Id == (int)a[0] && x.Version == (long)a[1]); return row != null && Rows.Remove(row); }
                    if (m.Name == "TryClaimSingleWork") return true;
                    if (m.Name == "ReleaseSingleWorkClaim" || m.Name == "DeleteOldCompleted") return null;
                    if (m.Name == "GetDueForProcessing") return Rows.Where(x => x.IsActive() && x.NextAttemptAt <= (DateTime)a[0]).Select(Clone).ToList();
                    throw new AssertionException(m.Name);
                });
                _events = Stub<IEventAggregator>((m, a) => { Events.Add(a[0]); return null; });
                _authors = Stub<IAuthorService>((m, a) =>
                {
                    if (m.Name == "FindByProviderId") return Author.Id > 0 ? Author : null;
                    if (m.Name == "GetAuthor") return Author;
                    if (m.Name == "AddAuthor") { Assert.That(a[1], Is.False); Author.Id = 10; return Author; }
                    if (m.Name == "UpdateAuthorProgressiveSettings") return Author;
                    if (m.Name == "EnsureMediaTypeMonitoring") return null;
                    throw new AssertionException(m.Name);
                });
                _books = Stub<IBookService>((m, a) =>
                {
                    if (m.Name == "FindByProviderId") return Books.FirstOrDefault(x => x.GoodreadsWorkId == (string)a[1] && x.MediaType == (BookMediaType)a[2]);
                    if (m.Name == "AddBook") { var book = (Book)a[0]; Assert.That(a[1], Is.False); book.Id = Books.Count + 20; foreach (var edition in book.Editions) { edition.Id = book.Id + 100; edition.BookId = book.Id; } Books.Add(book); return book; }
                    throw new AssertionException("Catalog read: " + m.Name);
                });
                _info = Stub<IProvideBookInfo>((m, a) =>
                {
                    Assert.That(m.Name, Is.EqualTo("GetBookInfo"));
                    DuringFetch?.Invoke();
                    if (Failure != null) throw Failure;
                    return Tuple.Create("gr:7", new Book { Title = "Requested", GoodreadsWorkId = (string)a[0], Author = Author, MediaType = (BookMediaType)a[1], Editions = new List<Edition> { new Edition { ForeignEditionId = "gr:77", Title = "Requested", Language = "eng", ReadingFormatId = (BookMediaType)a[1] == BookMediaType.Ebook ? 3 : 2 } } }, new List<Author> { Author });
                });
                Restart();
            }
            private static PendingAuthorImport Clone(PendingAuthorImport item) => item == null ? null : JsonConvert.DeserializeObject<PendingAuthorImport>(JsonConvert.SerializeObject(item));
            public void Restart()
            {
                Pending = new PendingAuthorImportService(_repository, _authors, _events, LogManager.GetCurrentClassLogger());
                var exclusions = Stub<IImportListExclusionService>((m,a) => m.Name == "FindByForeignId" ? new List<ImportListExclusion>() : null);
                var profiles = Stub<IMetadataProfileService>((m,a) => m.Name == "Exists" ? (object)true : new MetadataProfile { Id = 1, AllowedLanguages = "eng" });
                var searches = Stub<IBookAddedService>((m,a) => { Assert.That(m.Name, Is.EqualTo("SearchForBook")); Searches.Add((int)a[0]); Books.Single(x => x.Id == (int)a[0]).AddOptions.SearchForNewBook = false; return null; });
                Add = new AddBookService(_authors, Stub<IAuthorLibraryService>(), _books, Stub<IBookMonitoredService>(), searches, _info, exclusions, Stub<ISeriesBookLinkService>(), Stub<ISeriesService>(), Stub<IProvideAuthorInfo>(), Stub<IBuildFileNames>(), Stub<IMonitoringService>(), Stub<IEditionService>(), new EditionSelector(LogManager.GetCurrentClassLogger()), new EditionMetadataProfileFilter(new TestTermMatcherService()), profiles, LogManager.GetCurrentClassLogger(), Pending);
                Worker = new ProcessPendingImportsCommandHandler(Pending, Stub<IAuthorLibraryService>(), _authors, _books, _info, Stub<IManageCommandQueue>((m,a) => {
                    if (FailSearch) throw new InvalidOperationException("Command queue unavailable");
                    Searches.AddRange(((BookSearchCommand)a[0]).BookIds);
                    return null;
                }), _events, LogManager.GetCurrentClassLogger(), Add);
            }
            public Book Request(BookMediaType format = BookMediaType.Ebook, string id = "gr:42")
            {
                var request = new Book { Title = "Requested", GoodreadsWorkId = id, MediaType = format, AnyEditionOk = false,
                    Author = new Author { GoodreadsAuthorId = "gr:7", Name = "Requested Author", AddOptions = new AddAuthorOptions { Monitor = MonitorTypes.SpecificBook }, EbookMetadataProfileId = 1, AudiobookMetadataProfileId = 1, EbookQualityProfileId = 2, AudiobookQualityProfileId = 3, EbookRootFolderPath = "/ebooks", AudiobookRootFolderPath = "/audio", Tags = new HashSet<int> { 8 }, EbookTags = new HashSet<int> { 9 } },
                    Editions = new List<Edition> { new Edition { ForeignEditionId = "gr:77", ManualAdd = true, Monitored = true } }, AddOptions = new AddBookOptions { SearchForNewBook = true } };
                request.SetMonitored(true); return request;
            }
            public void Process() { foreach (var row in Rows.Where(x => x.IsActive())) row.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1); Worker.Execute(new ProcessPendingImportsCommand()); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task admission_persists_positive_id_and_restart_retries_only_exact_work(bool unavailable)
        {
            var h = new Harness();
            if (unavailable) h.Failure = new MetadataServerUnavailableException("cooldown", TimeSpan.FromSeconds(60));
            var pending = Assert.ThrowsAsync<PendingBookRequestException>(() => h.Add.AddBook(h.Request()));
            Assert.That(pending.PendingId, Is.GreaterThan(0));
            var row = h.Rows.Single(); Assert.That(row.SingleWorkOnly, Is.True);
            var payload = PendingSingleWorkRequest.Read(row.SingleWorkRequests).Single();
            Assert.That(payload.ToBook().Editions.Single().ForeignEditionId, Is.EqualTo("gr:77"));
            Assert.That(payload.Settings.EbookRootFolderPath, Is.EqualTo("/ebooks"));
            Assert.That(payload.Settings.EbookTags, Does.Contain(9));
            h.Restart(); h.Process();
            Assert.That(h.Rows.Single().OverallStatus, Is.EqualTo(PendingImportStatus.Retrying));
            Assert.That(h.Books, Is.Empty); Assert.That(h.Author.Id, Is.Zero); Assert.That(h.Searches, Is.Empty);
            h.Failure = null; h.Restart(); h.Process();
            Assert.That(h.Rows, Is.Empty); Assert.That(h.Books.Single().GoodreadsWorkId, Is.EqualTo("gr:42"));
            Assert.That(h.Books.Single().IsMonitored(), Is.True); Assert.That(h.Searches, Is.EqualTo(new[] { 20 }));
            // Replaying a completed local materialization does not depend on remote availability or repeat search.
            h.Failure = new BookMetadataPendingException("gr:42");
            await h.Add.ReplaySingleWork(payload.ToBook(), () => true);
            Assert.That(h.Books.Count, Is.EqualTo(1)); Assert.That(h.Searches, Is.EqualTo(new[] { 20 }));
        }

        [Test]
        public void different_requests_have_independent_cancellation_and_duplicate_admission_reuses_id()
        {
            var h = new Harness();
            var first = Assert.ThrowsAsync<PendingBookRequestException>(() => h.Add.AddBook(h.Request())).PendingId;
            Assert.That(Assert.ThrowsAsync<PendingBookRequestException>(() => h.Add.AddBook(h.Request())).PendingId, Is.EqualTo(first));
            var second = Assert.ThrowsAsync<PendingBookRequestException>(() => h.Add.AddBook(h.Request(BookMediaType.Audiobook))).PendingId;
            Assert.That(second, Is.Not.EqualTo(first)); h.Pending.Cancel(first);
            Assert.That(h.Rows.Single(x => x.Id == second).IsActive(), Is.True);
            h.Failure = null; h.Process();
            Assert.That(h.Books.Single().MediaType, Is.EqualTo(BookMediaType.Audiobook));
        }

        [Test]
        public void search_enqueue_failure_after_materialization_retains_search_intent()
        {
            var h = new Harness();
            Assert.ThrowsAsync<PendingBookRequestException>(() => h.Add.AddBook(h.Request()));
            h.Failure = null; h.FailSearch = true; h.Process();
            Assert.That(h.Books.Count, Is.EqualTo(1)); Assert.That(h.Rows.Single().IsActive(), Is.True);
            Assert.That(h.Searches, Is.Empty);
            h.Restart(); h.FailSearch = false; h.Failure = new BookMetadataPendingException("gr:42"); h.Process();
            Assert.That(h.Rows, Is.Empty); Assert.That(h.Books.Count, Is.EqualTo(1));
            Assert.That(h.Searches, Is.EqualTo(new[] { 20 }));
        }

        [Test]
        public void cancellation_during_metadata_fetch_prevents_materialization_and_search()
        {
            var h = new Harness();
            var id = Assert.ThrowsAsync<PendingBookRequestException>(() => h.Add.AddBook(h.Request())).PendingId;
            h.Failure = null; h.DuringFetch = () => h.Pending.Cancel(id); h.Process();
            Assert.That(h.Books, Is.Empty); Assert.That(h.Searches, Is.Empty); Assert.That(h.Author.Id, Is.Zero);
            Assert.That(h.Rows.Single().LastError, Is.EqualTo("Cancelled by user"));
        }

        [Test]
        public void missing_pinned_edition_does_not_materialize_a_substitute_work_edition()
        {
            var h = new Harness { Failure = null }; var request = h.Request();
            request.Editions.Single().ForeignEditionId = "gr:999";
            Assert.ThrowsAsync<PendingBookRequestException>(() => h.Add.AddBook(request));
            Assert.That(h.Books, Is.Empty); Assert.That(h.Rows.Single().IsActive(), Is.True);
        }

        [Test]
        public void invalid_metadata_profile_is_actionable_before_metadata_admission()
        {
            var h = new Harness(); var request = h.Request(); request.Author.EbookMetadataProfileId = null;
            Assert.ThrowsAsync<FluentValidation.ValidationException>(() => h.Add.AddBook(request));
            Assert.That(h.Rows, Is.Empty); Assert.That(h.Books, Is.Empty);
        }

        [Test]
        public void malformed_metadata_response_remains_retryable_despite_invalid_in_message()
        {
            var h = new Harness { Failure = new NzbDrone.Core.MetadataSource.BookInfo.BookInfoException("Invalid V5 work response") };
            Assert.ThrowsAsync<PendingBookRequestException>(() => h.Add.AddBook(h.Request()));
            h.Process(); Assert.That(h.Rows.Single().OverallStatus, Is.EqualTo(PendingImportStatus.Retrying));
            Assert.That(h.Books, Is.Empty);
        }

        [Test]
        public void real_missing_remains_terminal_and_does_not_enqueue()
        {
            var h = new Harness { Failure = new BookNotFoundException("gr:42") };
            Assert.ThrowsAsync<BookNotFoundException>(() => h.Add.AddBook(h.Request())); Assert.That(h.Rows, Is.Empty);
            h.Failure = new BookMetadataPendingException("gr:42");
            Assert.ThrowsAsync<PendingBookRequestException>(() => h.Add.AddBook(h.Request()));
            h.Failure = new BookNotFoundException("gr:42"); h.Process();
            Assert.That(h.Rows.Single().OverallStatus, Is.EqualTo(PendingImportStatus.Failed)); Assert.That(h.Books, Is.Empty);
        }
    }
}

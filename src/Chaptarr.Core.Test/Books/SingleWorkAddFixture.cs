using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FluentValidation;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Services;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Profiles.Metadata;

namespace Chaptarr.Core.Test.Books
{
    [TestFixture]
    public class SingleWorkAddFixture
    {
        public class Proxy<T> : DispatchProxy where T : class
        {
            public Func<MethodInfo, object[], object> Handler;
            protected override object Invoke(MethodInfo method, object[] args) => Handler(method, args);
        }

        private static T Stub<T>(Func<MethodInfo, object[], object> handler = null) where T : class
        {
            var proxy = DispatchProxy.Create<T, Proxy<T>>();
            ((Proxy<T>)(object)proxy).Handler = handler ?? ((m, a) => throw new AssertionException("Unexpected catalog-wide call: " + typeof(T).Name + "." + m.Name));
            return proxy;
        }

        [TestCase(BookMediaType.Audiobook, true, false)]
        [TestCase(BookMediaType.Audiobook, false, false)]
        [TestCase(BookMediaType.Ebook, true, false)]
        [TestCase(BookMediaType.Ebook, false, false)]
        [TestCase(BookMediaType.Ebook, true, true)]
        [TestCase(BookMediaType.Audiobook, true, true)]
        public async Task add_fetches_only_requested_work_and_searches_only_its_id(BookMediaType format, bool newAuthor, bool missingMetadata)
        {
            var searched = new List<int>();
            var exclusionChecked = false;
            var author = new Author { Id = newAuthor ? 0 : 10, Name = "Author", HardcoverAuthorId = "hc:7", Monitored = true,
                AudiobookMonitored = true, EbookMonitored = true, AudiobookMetadataProfileId = 1, EbookMetadataProfileId = 1 };
            var remote = new Book { Title = "Requested", HardcoverBookId = "hc:42", MediaType = format, Author = author,
                Editions = new List<Edition> { new Edition { Title = "Requested", ForeignEditionId = "hc:edition:77", Language = "eng", ReadingFormatId = format == BookMediaType.Audiobook ? 2 : 3 } } };
            var request = new Book { Title = "Pending Import", HardcoverBookId = "hc:42", MediaType = format,
                Author = new Author { HardcoverAuthorId = "hc:7", AddOptions = new AddAuthorOptions { Monitor = MonitorTypes.SpecificBook },
                    AudiobookMonitored = true, EbookMonitored = true, AudiobookMetadataProfileId = 1, EbookMetadataProfileId = 1 },
                AnyEditionOk = true, AddOptions = new AddBookOptions { SearchForNewBook = true } };
            request.SetMonitored(true);
            var authorService = Stub<IAuthorService>((m,a) => {
                if (m.Name == "FindByProviderId") return newAuthor && author.Id == 0 ? null : author;
                if (m.Name == "GetAuthor") return author;
                if (m.Name == "EnsureMediaTypeMonitoring") return null;
                if (m.Name == "AddAuthor") { Assert.That(a[1], Is.False); author.Id = 10; return author; }
                if (m.Name == "UpdateAuthorProgressiveSettings") return author;
                throw new AssertionException(m.Name);
            });
            var bookService = Stub<IBookService>((m,a) => {
                if (m.Name == "FindByProviderId") return null;
                if (m.Name == "AddBook") { Assert.That(exclusionChecked, Is.True); Assert.That(a[1], Is.False); var b=(Book)a[0]; b.Id=20; return b; }
                throw new AssertionException("Unexpected author catalog read: " + m.Name);
            });
            var info = Stub<IProvideBookInfo>((m,a) => { Assert.That(m.Name, Is.EqualTo("GetBookInfo")); Assert.That(a[0], Is.EqualTo("hc:42")); Assert.That(a[1], Is.EqualTo(format)); return Tuple.Create("hc:7",remote,new List<Author>{author}); });
            var exclusions = Stub<IImportListExclusionService>((m,a) => { if(m.Name=="FindByForeignId") { exclusionChecked=true; return new List<ImportListExclusion>(); } if(m.Name=="Delete") return null; throw new AssertionException(m.Name); });
            var profiles = Stub<IMetadataProfileService>((m,a)=>m.Name=="Exists" ? (object)true : new MetadataProfile { Id=1, AllowedLanguages="eng" });
            var added = Stub<IBookAddedService>((m,a)=>{Assert.That(m.Name,Is.EqualTo("SearchForBook")); searched.Add((int)a[0]); return null;});
            var service = new AddBookService(authorService,Stub<IAuthorLibraryService>(),bookService,Stub<IBookMonitoredService>(),added,info,exclusions,
                Stub<ISeriesBookLinkService>(),Stub<ISeriesService>(),Stub<IProvideAuthorInfo>(),Stub<IBuildFileNames>(),Stub<IMonitoringService>(),
                Stub<IEditionService>(),new EditionSelector(LogManager.GetCurrentClassLogger()),new EditionMetadataProfileFilter(new TestTermMatcherService()),profiles,LogManager.GetCurrentClassLogger());
            if (missingMetadata)
            {
                remote.Editions.Clear();
                Assert.ThrowsAsync<ValidationException>(() => service.AddBook(request));
                Assert.That(author.Id, Is.Zero, "Failed metadata must not create an author or trigger catalog import.");
                Assert.That(searched, Is.Empty);
                return;
            }

            var result=await service.AddBook(request);
            Assert.That(result.Id,Is.EqualTo(20)); Assert.That(result.AuthorId,Is.EqualTo(10)); Assert.That(result.IsMonitored(),Is.True);
            Assert.That(result.MediaType,Is.EqualTo(format)); Assert.That(searched,Is.EqualTo(new[]{20}));
        }
        [TestCase(true)]
        [TestCase(false)]
        public void book_added_event_never_queues_author_catalog_refresh(bool refresh)
        {
            var commands = new List<NzbDrone.Core.Messaging.Commands.Command>();
            var queue = Stub<NzbDrone.Core.Messaging.Commands.IManageCommandQueue>((method,args) => {
                Assert.That(method.Name, Is.EqualTo("Push"));
                commands.Add((NzbDrone.Core.Messaging.Commands.Command)args[0]);
                return new NzbDrone.Core.Messaging.Commands.CommandModel();
            });
            new BookAddedHandler(queue).Handle(new NzbDrone.Core.Books.Events.BookAddedEvent(new Book { Id=42, AuthorId=7 }, refresh));
            Assert.That(commands.Exists(c => c is NzbDrone.Core.Books.Commands.RefreshAuthorCommand), Is.False);
            Assert.That(commands.Exists(c => c is NzbDrone.Core.MediaCover.Commands.DownloadBookMediaCommand), Is.True);
            Assert.That(commands.Exists(c => c is NzbDrone.Core.Books.Commands.RefreshBookCommand), Is.EqualTo(refresh));
        }
    }
}

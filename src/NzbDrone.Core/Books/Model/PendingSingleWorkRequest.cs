using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using NzbDrone.Core.Books.Services;

namespace NzbDrone.Core.Books
{
    // A versioned admission intent, never a synthetic library Book or remote catalog.
    public class PendingSingleWorkRequest
    {
        public int PayloadVersion { get; set; } = 1;
        public string WorkProviderId { get; set; }
        public string AuthorProviderId { get; set; }
        public string Title { get; set; }
        public BookMediaType MediaType { get; set; }
        public bool Monitored { get; set; }
        public bool AnyEditionOk { get; set; }
        public int? WantedNarratorId { get; set; }
        public bool Search { get; set; }
        public List<string> EditionProviderIds { get; set; } = new List<string>();
        public List<string> PinnedEditionProviderIds { get; set; } = new List<string>();
        public MonitoringConfig Settings { get; set; }
        [JsonIgnore]
        public string Key => MediaType + ":" + WorkProviderId;
        public string GetRequestKey()
        {
            Settings.Tags = Settings.Tags == null ? null : new HashSet<int>(Settings.Tags.OrderBy(x => x));
            Settings.AudiobookTags = Settings.AudiobookTags == null ? null : new HashSet<int>(Settings.AudiobookTags.OrderBy(x => x));
            Settings.EbookTags = Settings.EbookTags == null ? null : new HashSet<int>(Settings.EbookTags.OrderBy(x => x));
            return "single:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this))));
        }

        public static List<PendingSingleWorkRequest> Read(string json) => string.IsNullOrWhiteSpace(json)
            ? new List<PendingSingleWorkRequest>()
            : JsonConvert.DeserializeObject<List<PendingSingleWorkRequest>>(json) ?? throw new InvalidOperationException("Invalid pending single-work payload.");

        public static PendingSingleWorkRequest Create(Book book, string workId, string authorId, MonitoringConfig settings)
        {
            return new PendingSingleWorkRequest
            {
                WorkProviderId = workId,
                AuthorProviderId = authorId,
                Title = book.Title,
                MediaType = book.MediaType,
                Monitored = book.IsMonitored(),
                AnyEditionOk = book.AnyEditionOk,
                WantedNarratorId = book.WantedNarratorId,
                Search = book.AddOptions?.SearchForNewBook == true,
                EditionProviderIds = book.Editions?.Where(x => x.Monitored).SelectMany(BookEditionIdentity.GetEditionProviderIds).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList() ?? new List<string>(),
                PinnedEditionProviderIds = book.Editions?.Where(x => x.ManualAdd && x.Monitored).SelectMany(BookEditionIdentity.GetEditionProviderIds).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList() ?? new List<string>(),
                Settings = JsonConvert.DeserializeObject<MonitoringConfig>(JsonConvert.SerializeObject(settings))
            };
        }

        public Book ToBook()
        {
            if (PayloadVersion != 1 || Settings == null || string.IsNullOrWhiteSpace(WorkProviderId) || string.IsNullOrWhiteSpace(AuthorProviderId))
                throw new ArgumentException("Unsupported or invalid pending single-work request payload.");
            var c = Settings;
            var author = new Author
            {
                Name = c.AuthorName,
                AddOptions = new AddAuthorOptions { Monitor = MonitorTypes.SpecificBook },
                AudiobookMonitored = c.AudiobookMonitored,
                EbookMonitored = c.EbookMonitored,
                AudiobookMonitorNewItems = c.AudiobookMonitorNewItems,
                EbookMonitorNewItems = c.EbookMonitorNewItems,
                AudiobookQualityProfileId = c.AudiobookQualityProfileId,
                EbookQualityProfileId = c.EbookQualityProfileId,
                AudiobookMetadataProfileId = c.AudiobookMetadataProfileId,
                EbookMetadataProfileId = c.EbookMetadataProfileId,
                AudiobookRootFolderPath = c.AudiobookRootFolderPath,
                EbookRootFolderPath = c.EbookRootFolderPath,
                Tags = c.Tags ?? new HashSet<int>(),
                AudiobookTags = c.AudiobookTags ?? new HashSet<int>(),
                EbookTags = c.EbookTags ?? new HashSet<int>()
            };
            SetAuthorId(author, AuthorProviderId);
            var book = new Book
            {
                Title = Title, Author = author, MediaType = MediaType, AnyEditionOk = AnyEditionOk,
                WantedNarratorId = WantedNarratorId, AddOptions = new AddBookOptions { SearchForNewBook = Search },
                RemoteProviderIds = new HashSet<string> { WorkProviderId },
                Editions = EditionProviderIds.Select(id => new Edition { ForeignEditionId = id, ManualAdd = PinnedEditionProviderIds.Contains(id), Monitored = true }).ToList()
            };
            if (WorkProviderId.StartsWith("hc:")) book.HardcoverBookId = WorkProviderId;
            else if (WorkProviderId.StartsWith("gr:")) book.GoodreadsWorkId = WorkProviderId;
            else if (WorkProviderId.StartsWith("ol:")) book.OpenLibraryWorkId = WorkProviderId;
            else if (WorkProviderId.StartsWith("az:")) book.AudibleASIN = WorkProviderId;
            else throw new ArgumentException("Unsupported single-work provider ID.");
            book.SetMonitored(Monitored);
            return book;
        }

        private static void SetAuthorId(Author author, string id)
        {
            if (id.StartsWith("hc:")) author.HardcoverAuthorId = id;
            else if (id.StartsWith("gr:")) author.GoodreadsAuthorId = id;
            else if (id.StartsWith("ol:")) author.OpenLibraryAuthorId = id;
            else if (id.StartsWith("az:")) author.AudnexusAuthorId = id;
            else throw new ArgumentException("Unsupported single-work author provider ID.");
        }
    }
}

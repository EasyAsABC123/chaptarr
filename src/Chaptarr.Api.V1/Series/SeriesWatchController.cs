using System;
using System.Linq;
using System.Text.RegularExpressions;
using Chaptarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.SeriesWatch;
using NzbDrone.Core.Messaging.Commands;

namespace Chaptarr.Api.V1.Series
{
    [V1ApiController("series/watch")]
    public class SeriesWatchController : Controller
    {
        private static readonly object Admission = new();
        private readonly IImportListFactory _lists;
        private readonly IManageCommandQueue _commands;

        public SeriesWatchController(IImportListFactory lists, IManageCommandQueue commands)
        {
            _lists = lists;
            _commands = commands;
        }

        [HttpGet("capabilities")]
        public object Capabilities() => new { version = 1, providers = new[] { "hc", "gr" }, futureBooks = true };

        private static bool ValidIdentity(string providerId, string mediaType) =>
            Regex.IsMatch(providerId ?? "", @"^(hc|gr):[1-9]\d*$") &&
            int.TryParse(providerId.Substring(3), out _) && (mediaType == "ebook" || mediaType == "audiobook");

        private ImportListDefinition Find(string providerId, string mediaType) => _lists.All().SingleOrDefault(list =>
            list.Implementation == nameof(SeriesWatchImportList) && list.Settings is SeriesWatchImportListSettings settings &&
            settings.ProviderId == providerId && (mediaType == "ebook" ? settings.MonitorEbooks : settings.MonitorAudiobooks));

        private static object Resource(ImportListDefinition list)
        {
            if (list == null) return new { enabled = false, watchId = (int?)null };
            var settings = (SeriesWatchImportListSettings)list.Settings;
            return new { watchId = (int?)list.Id, enabled = list.EnableAutomaticAdd, settings.ProviderId,
                mediaType = settings.MonitorAudiobooks ? "audiobook" : "ebook", settings.IncludeExtras,
                managementPath = "/settings/importlists", futureBooks = true };
        }

        [HttpGet]
        public IActionResult Get([FromQuery] string providerId, [FromQuery] string mediaType)
        {
            if (!ValidIdentity(providerId, mediaType)) return BadRequest();
            lock (Admission) return Ok(Resource(Find(providerId, mediaType)));
        }

        [HttpPut]
        public IActionResult Put([FromBody] SeriesWatchRequest request)
        {
            if (request == null || !ValidIdentity(request.ProviderId, request.MediaType) || request.Enabled == null)
                return BadRequest(new { message = "Specify providerId, mediaType and enabled." });
            lock (Admission)
            {
                var existing = Find(request.ProviderId, request.MediaType);
                if (request.Enabled == false)
                {
                    if (existing != null)
                    {
                        existing.EnableAutomaticAdd = false;
                        _lists.Update(existing);
                    }

                    // Disabling discovery never unmonitors books or removes downloaded files.
                    return Ok(Resource(existing));
                }

                if (existing?.EnableAutomaticAdd == true)
                {
                    var settings = (SeriesWatchImportListSettings)existing.Settings;
                    if (settings.IncludeExtras != request.IncludeExtras)
                        return Conflict(new { message = "This series is already watched with a different extras policy. Manage it in Chaptarr." });
                    return Ok(Resource(existing));
                }

                var audiobook = request.MediaType == "audiobook";
                var definition = new ImportListDefinition
                {
                    Id = existing?.Id ?? 0,
                    Name = $"Series watch {request.ProviderId} ({request.MediaType})",
                    Implementation = nameof(SeriesWatchImportList),
                    ConfigContract = nameof(SeriesWatchImportListSettings),
                    EnableAutomaticAdd = true,
                    ShouldMonitor = ImportListMonitorType.SpecificBook,
                    ShouldMonitorExisting = true,
                    ShouldSearch = request.SearchForMissing,
                    // New books are discovered by this series list, never an author-wide future policy.
                    MonitorNewItems = NewItemMonitorTypes.None,
                    Settings = new SeriesWatchImportListSettings
                    {
                        ProviderId = request.ProviderId,
                        IncludeExtras = request.IncludeExtras,
                        MonitorAudiobooks = audiobook,
                        MonitorEbooks = !audiobook,
                        AudiobookRootFolderPath = audiobook ? request.RootFolder : null,
                        EbookRootFolderPath = audiobook ? null : request.RootFolder,
                        AudiobookQualityProfileId = audiobook ? request.QualityProfileId : 0,
                        EbookQualityProfileId = audiobook ? 0 : request.QualityProfileId,
                        AudiobookMetadataProfileId = audiobook ? request.MetadataProfileId : 0,
                        EbookMetadataProfileId = audiobook ? 0 : request.MetadataProfileId
                    }
                };
                var validation = definition.Settings.Validate();
                if (!validation.IsValid) return BadRequest(new { message = "Invalid series watch settings." });
                var test = _lists.Test(definition);
                if (!test.IsValid) return BadRequest(new { message = "Series catalog or destination could not be validated in Chaptarr." });
                if (existing == null) definition = _lists.Create(definition);
                else _lists.Update(definition);
                _commands.Push(new ImportListSyncCommand(definition.Id));
                return Ok(Resource(definition));
            }
        }
    }

    public class SeriesWatchRequest
    {
        public string ProviderId { get; set; }
        public string MediaType { get; set; }
        public bool? Enabled { get; set; }
        public bool IncludeExtras { get; set; }
        public string RootFolder { get; set; }
        public int QualityProfileId { get; set; }
        public int MetadataProfileId { get; set; }
        public bool SearchForMissing { get; set; }
    }
}

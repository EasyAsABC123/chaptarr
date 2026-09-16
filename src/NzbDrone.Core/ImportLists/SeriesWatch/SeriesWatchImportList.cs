using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Goodreads;
using System.Net;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tags;

namespace NzbDrone.Core.ImportLists.SeriesWatch
{
    public class SeriesWatchImportList : ImportListBase<SeriesWatchImportListSettings>
    {
        private readonly ISeriesWatchCatalog _seriesInfo;
        private readonly Lazy<IQualityProfileService> _qualityProfileService;
        private readonly Lazy<IMetadataProfileService> _metadataProfileService;
        private readonly Lazy<ITagService> _tagService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IRootFolderSettingsResolver _rootFolderSettingsResolver;

        public override string Name => "Series Watch";
        public override ImportListType ListType => ImportListType.Other;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(12);

        public SeriesWatchImportList(ISeriesWatchCatalog seriesInfo,
            IImportListStatusService importListStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Lazy<IQualityProfileService> qualityProfileService,
            Lazy<IMetadataProfileService> metadataProfileService,
            Lazy<ITagService> tagService,
            IRootFolderService rootFolderService,
            IRootFolderSettingsResolver rootFolderSettingsResolver,
            Logger logger)
            : base(importListStatusService, configService, parsingService, logger)
        {
            _seriesInfo = seriesInfo;
            _qualityProfileService = qualityProfileService;
            _metadataProfileService = metadataProfileService;
            _tagService = tagService;
            _rootFolderService = rootFolderService;
            _rootFolderSettingsResolver = rootFolderSettingsResolver;
        }

        public override IList<ImportListItemInfo> Fetch()
        {
            var result = new List<ImportListItemInfo>();

            try
            {
                result.AddRange(_seriesInfo.Fetch(Settings.ProviderId, Settings.IncludeExtras));

                _importListStatusService.RecordSuccess(Definition.Id);
            }
            catch
            {
                _importListStatusService.RecordFailure(Definition.Id);
            }

            return CleanupListItems(result);
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            failures.AddIfNotNull(TestConnection());
            if (Settings.MonitorAudiobooks) ValidateProfiles(failures, Settings.AudiobookQualityProfileId, Settings.AudiobookMetadataProfileId, ProfileType.Audiobook, MetadataProfileType.Audiobook);
            if (Settings.MonitorEbooks) ValidateProfiles(failures, Settings.EbookQualityProfileId, Settings.EbookMetadataProfileId, ProfileType.Ebook, MetadataProfileType.Ebook);
            failures.AddRange(GoodreadsDualMediaImportListActions.TestRootFolderConfig(Settings, _rootFolderService, _rootFolderSettingsResolver));
        }

        private void ValidateProfiles(List<ValidationFailure> failures, int qualityId, int metadataId, ProfileType qualityType, MetadataProfileType metadataType)
        {
            if (qualityId < 0 || (qualityId > 0 && !_qualityProfileService.Value.GetByType(qualityType).Any(profile => profile.Id == qualityId)))
                failures.Add(new ValidationFailure("QualityProfile", "The quality profile must exist and match the requested format."));
            if (metadataId < 0 || (metadataId > 0 && !_metadataProfileService.Value.All().Any(profile => profile.Id == metadataId &&
                (profile.ProfileType == metadataType || profile.ProfileType == MetadataProfileType.General))))
                failures.Add(new ValidationFailure("MetadataProfile", "The metadata profile must exist and match the requested format."));
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            return GoodreadsDualMediaImportListActions.HandleRequestAction(action, _qualityProfileService, _metadataProfileService, _tagService)
                ?? base.RequestAction(action, query);
        }

        private ValidationFailure TestConnection()
        {
            try
            {
                _seriesInfo.Fetch(Settings.ProviderId, Settings.IncludeExtras);
                return null;
            }
            catch (HttpException e)
            {
                _logger.Warn(e, "Series catalog error");
                if (e.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new ValidationFailure(nameof(Settings.ProviderId), $"Series {Settings.ProviderId} not found");
                }

                return new ValidationFailure(nameof(Settings.ProviderId), $"Could not get series data");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to connect to series catalog");

                return new ValidationFailure(string.Empty, "Unable to connect to import list, check the log for more details");
            }
        }
    }
}

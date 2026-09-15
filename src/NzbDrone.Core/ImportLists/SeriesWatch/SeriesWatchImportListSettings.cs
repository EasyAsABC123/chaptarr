using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Goodreads;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.SeriesWatch
{
    public class SeriesWatchImportListValidator : AbstractValidator<SeriesWatchImportListSettings>
    {
        public SeriesWatchImportListValidator()
        {
            RuleFor(c => c.ProviderId).NotEmpty().Matches(@"^(hc|gr):[1-9]\d*$");
            RuleFor(c => c.IncludeExtras).Equal(true).When(c => c.ProviderId?.StartsWith("gr:") == true);
            this.AddDualMediaRules();
            RuleFor(c => c.MonitorAudiobooks).NotEqual(c => c.MonitorEbooks).WithMessage("Choose exactly one format per series watch.");
            RuleFor(c => c.ImportLimit).Equal(0).WithMessage("A series watch must include every selected catalog member.");
        }
    }

    public class SeriesWatchImportListSettings : IGoodreadsDualMediaImportListSettings
    {
        private static readonly SeriesWatchImportListValidator Validator = new();

        public SeriesWatchImportListSettings()
        {
            BaseUrl = "";
            MonitorAudiobooks = false;
            MonitorEbooks = true;
        }

        public string BaseUrl { get; set; }

        [FieldDefinition(0, Label = "Series Provider ID", HelpText = "Exact hc: or gr: series ID. Never a title.")]
        public string ProviderId { get; set; }

        [FieldDefinition(12, Label = "Include Extras", Type = FieldType.Checkbox, HelpText = "Include companion books and extras. Required for Goodreads, which does not classify primary books here.")]
        public bool IncludeExtras { get; set; }

        [FieldDefinition(1, Label = "Monitor Audiobooks", Type = FieldType.Checkbox, HelpText = "When enabled, audiobook items will be created/monitored for books from this series.")]
        public bool MonitorAudiobooks { get; set; }

        [FieldDefinition(2, Label = "Monitor Ebooks", Type = FieldType.Checkbox, HelpText = "When enabled, ebook items will be created/monitored for books from this series.")]
        public bool MonitorEbooks { get; set; }

        public int ImportLimit { get; set; }

        [FieldDefinition(4, Label = "Audiobook Quality Profile", Type = FieldType.Select, SelectOptionsProviderAction = "getAudiobookQualityProfiles", HelpText = "Quality profile used when importing audiobooks from this series.")]
        public int AudiobookQualityProfileId { get; set; }

        [FieldDefinition(5, Label = "Ebook Quality Profile", Type = FieldType.Select, SelectOptionsProviderAction = "getEbookQualityProfiles", HelpText = "Quality profile used when importing ebooks from this series.")]
        public int EbookQualityProfileId { get; set; }

        [FieldDefinition(6, Label = "Audiobook Metadata Profile", Type = FieldType.Select, SelectOptionsProviderAction = "getAudiobookMetadataProfiles", HelpText = "Metadata profile used when importing audiobooks from this series.")]
        public int AudiobookMetadataProfileId { get; set; }

        [FieldDefinition(7, Label = "Ebook Metadata Profile", Type = FieldType.Select, SelectOptionsProviderAction = "getEbookMetadataProfiles", HelpText = "Metadata profile used when importing ebooks from this series.")]
        public int EbookMetadataProfileId { get; set; }

        [FieldDefinition(8, Label = "Audiobook Root Folder", Type = FieldType.Path, HelpText = "Root folder used when importing audiobooks from this series.")]
        public string AudiobookRootFolderPath { get; set; }

        [FieldDefinition(9, Label = "Ebook Root Folder", Type = FieldType.Path, HelpText = "Root folder used when importing ebooks from this series.")]
        public string EbookRootFolderPath { get; set; }

        [FieldDefinition(10, Label = "Audiobook Tags", Type = FieldType.TagSelect, SelectOptionsProviderAction = "getTags", HelpText = "Optional: tags to apply when importing audiobooks from this series.")]
        public List<int> AudiobookTags { get; set; } = new();

        [FieldDefinition(11, Label = "Ebook Tags", Type = FieldType.TagSelect, SelectOptionsProviderAction = "getTags", HelpText = "Optional: tags to apply when importing ebooks from this series.")]
        public List<int> EbookTags { get; set; } = new();

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}

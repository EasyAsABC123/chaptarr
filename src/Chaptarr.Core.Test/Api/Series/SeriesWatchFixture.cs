using System;
using System.Collections.Generic;
using System.Linq;
using Chaptarr.Api.V1.Series;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.SeriesWatch;
using NzbDrone.Core.Messaging.Commands;
using static Chaptarr.Core.Test.Api.Series.SeriesMonitorFixture;

namespace Chaptarr.Core.Test.Api.Series
{
    [TestFixture]
    public class SeriesWatchFixture
    {
        private List<ImportListDefinition> _saved;
        private List<ImportListSyncCommand> _commands;
        private SeriesWatchController _controller;
        private bool _valid;

        [SetUp]
        public void Setup()
        {
            _saved = new(); _commands = new(); _valid = true;
            _controller = new SeriesWatchController(Proxy<IImportListFactory>((method, args) =>
            {
                switch (method)
                {
                    case "All": return _saved;
                    case "Test": return _valid ? new ValidationResult() : new ValidationResult(new[] { new ValidationFailure("catalog", "unavailable") });
                    case "Create":
                        var definition = (ImportListDefinition)args[0]; definition.Id = _saved.Count + 1; _saved.Add(definition); return definition;
                    case "Update": var updated = (ImportListDefinition)args[0]; _saved[_saved.FindIndex(item => item.Id == updated.Id)] = updated; return null;
                    default: throw new NotImplementedException(method);
                }
            }), Proxy<IManageCommandQueue>((method, args) =>
            {
                if (method != "Push") throw new NotImplementedException(method);
                _commands.Add((ImportListSyncCommand)args[0]); return new CommandModel();
            }));
        }
        private static SeriesWatchRequest Request(string mediaType = "audiobook") => new()
        {
            ProviderId = "hc:988", MediaType = mediaType, Enabled = true, RootFolder = "/books", QualityProfileId = 1,
            MetadataProfileId = 1, SearchForMissing = true
        };

        [Test]
        public void retries_reuse_the_watch_and_stop_does_not_change_books()
        {
            Assert.That(_controller.Put(Request()), Is.TypeOf<OkObjectResult>());
            Assert.That(_controller.Put(Request()), Is.TypeOf<OkObjectResult>());
            Assert.That(_saved.Count, Is.EqualTo(1));
            Assert.That(_commands.Count, Is.EqualTo(1));
            Assert.That(_saved[0].ShouldMonitor, Is.EqualTo(ImportListMonitorType.SpecificBook));
            Assert.That(_saved[0].MonitorNewItems, Is.EqualTo(NewItemMonitorTypes.None));
            var settings = (SeriesWatchImportListSettings)_saved[0].Settings;
            Assert.That(settings.MonitorAudiobooks, Is.True); Assert.That(settings.MonitorEbooks, Is.False);
            var stop = Request(); stop.Enabled = false;
            Assert.That(_controller.Put(stop), Is.TypeOf<OkObjectResult>());
            Assert.That(_saved[0].EnableAutomaticAdd, Is.False);
            Assert.That(_commands.Count, Is.EqualTo(1));
            Assert.That(_controller.Put(Request()), Is.TypeOf<OkObjectResult>());
            Assert.That(_saved.Count, Is.EqualTo(1));
            Assert.That(_commands.Count, Is.EqualTo(2));
        }
        [Test]
        public void two_formats_are_independent_watches()
        {
            _controller.Put(Request()); _controller.Put(Request("ebook"));
            Assert.That(_saved.Count, Is.EqualTo(2));
            Assert.That(((SeriesWatchImportListSettings)_saved[1].Settings).MonitorAudiobooks, Is.False);
        }
        [Test]
        public void invalid_catalog_or_destination_creates_nothing()
        {
            _valid = false;
            Assert.That(_controller.Put(Request()), Is.TypeOf<BadRequestObjectResult>());
            Assert.That(_saved, Is.Empty); Assert.That(_commands, Is.Empty);
        }
        [TestCase("988")]
        [TestCase("hc:0")]
        [TestCase("hc:99999999999999999999")]
        [TestCase("http://example.com")]
        public void invalid_provider_identity_is_rejected(string id)
        {
            var request = Request(); request.ProviderId = id;
            Assert.That(_controller.Put(request), Is.TypeOf<BadRequestObjectResult>());
            Assert.That(_saved, Is.Empty);
        }
        [Test]
        public void extras_changes_do_not_silently_expand_an_existing_watch()
        {
            _controller.Put(Request()); var changed = Request(); changed.IncludeExtras = true;
            Assert.That(_controller.Put(changed), Is.TypeOf<ConflictObjectResult>());
            Assert.That(((SeriesWatchImportListSettings)_saved.Single().Settings).IncludeExtras, Is.False);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Chaptarr.Core.Test.Books
{
    [TestFixture]
    public class PendingAuthorImportRepositoryRetryFixture
    {
        private sealed class StubEventAggregator : IEventAggregator
        {
            public void PublishEvent<TEvent>(TEvent @event)
                where TEvent : class, IEvent
            {
            }
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (TableMapping.Mapper.TableMap.Count == 0)
            {
                TableMapping.Map();
            }
        }

        [Test]
        public void due_query_should_select_transient_rows_at_or_beyond_the_legacy_attempt_ceiling()
        {
            WithRepository((repository, connectionString) =>
            {
                var due = repository.GetDueForProcessing(DateTime.UtcNow, 10);

                Assert.That(due.Select(x => x.Id), Is.EqualTo(new[] { 1, 4 }));
            });
        }

        [Test]
        public void request_update_and_delete_should_require_the_expected_version()
        {
            WithCasRepository((repository, connectionString) =>
            {
                var item = new PendingAuthorImport
                {
                    Id = 1,
                    ProviderId = "gr:1",
                    Version = 4,
                    AudiobookStatus = PendingImportStatus.Pending,
                    EbookStatus = PendingImportStatus.NotRequested,
                    OverallStatus = PendingImportStatus.Pending,
                    AudiobookBooksToMonitor = "[\"gr:first\",\"gr:second\"]",
                    AudiobookTags = "[1]",
                    EbookTags = "[]",
                    Tags = "[1]",
                    LastSelectedMediaType = "ebook",
                    NextAttemptAt = DateTime.UtcNow
                };

                Assert.That(repository.TryUpdateRequest(item, expectedVersion: 3), Is.False);
                Assert.That(repository.TryUpdateRequest(item, expectedVersion: 4), Is.True);
                Assert.That(item.Version, Is.EqualTo(5));

                using (var verifyUpdate = new SqliteConnection(connectionString))
                {
                    verifyUpdate.Open();
                    var tags = verifyUpdate.QuerySingle<TagProjection>(@"
                        SELECT ""AudiobookTags"", ""EbookTags"", ""Tags"", ""LastSelectedMediaType""
                        FROM ""PendingAuthorImport""
                        WHERE ""Id"" = 1;");
                    Assert.That(tags.AudiobookTags, Is.EqualTo("[1]"));
                    Assert.That(tags.EbookTags, Is.EqualTo("[]"));
                    Assert.That(tags.Tags, Is.EqualTo("[1]"));
                    Assert.That(tags.LastSelectedMediaType, Is.EqualTo("ebook"));
                }

                Assert.That(repository.TryDelete(item.Id, expectedVersion: 4), Is.False);
                Assert.That(repository.TryDelete(item.Id, expectedVersion: 5), Is.True);

                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                Assert.That(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"PendingAuthorImport\""), Is.Zero);
            });
        }

        [Test]
        public void single_work_claims_are_exclusive_recover_expired_leases_and_fence_cancelled_snapshots()
        {
            WithCasRepository((repository, cs) =>
            {
                using var db = new SqliteConnection(cs); db.Open();
                db.Execute("UPDATE PendingAuthorImport SET SingleWorkOnly=1,RequestKey='exact',NextAttemptAt=@Now", new { Now = DateTime.UtcNow.AddMinutes(-1) });
                var item = new PendingAuthorImport { Id = 1, Version = 4, SingleWorkOnly = true };
                var stale = new PendingAuthorImport { Id = 1, Version = 4, SingleWorkOnly = true };
                var now = DateTime.UtcNow;
                Assert.That(repository.TryClaimSingleWork(item, now), Is.True);
                Assert.That(repository.TryClaimSingleWork(stale, now), Is.False);
                db.Execute("UPDATE PendingAuthorImport SET LeaseExpiresAt=@Past", new { Past = now.AddSeconds(-1) });
                Assert.That(repository.TryClaimSingleWork(stale, now), Is.True);
                repository.ReleaseSingleWorkClaim(item);
                Assert.That(db.ExecuteScalar<string>("SELECT LockedBy FROM PendingAuthorImport"), Is.EqualTo(stale.LockedBy));
                db.Execute("UPDATE PendingAuthorImport SET OverallStatus=6,Version=5");
                Assert.That(repository.TryDelete(1, item.Version), Is.False);
                repository.ReleaseSingleWorkClaim(stale);
                Assert.That(repository.TryClaimSingleWork(item, now), Is.False);
            });
        }

        private static void WithCasRepository(Action<PendingAuthorImportRepository, string> action)
        {
            var databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"pending_author_cas_{Guid.NewGuid():N}.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    connection.Execute(@"
                        CREATE TABLE ""PendingAuthorImport"" (
                            ""Id"" INTEGER PRIMARY KEY,
                            ""ProviderId"" TEXT,
                            ""RequestKey"" TEXT,
                            ""SingleWorkOnly"" INTEGER,
                            ""SingleWorkRequests"" TEXT,
                            ""LockedBy"" TEXT,
                            ""LockedAt"" DATETIME,
                            ""LeaseExpiresAt"" DATETIME,
                            ""DiscoveredAuthorFolderPath"" TEXT,
                            ""AudiobookStatus"" INTEGER,
                            ""EbookStatus"" INTEGER,
                            ""OverallStatus"" INTEGER,
                            ""AudiobookMonitored"" INTEGER,
                            ""AudiobookMonitorNewItems"" INTEGER,
                            ""AudiobookMonitorExistingMode"" INTEGER,
                            ""AudiobookQualityProfileId"" INTEGER,
                            ""AudiobookMetadataProfileId"" INTEGER,
                            ""AudiobookRootFolderPath"" TEXT,
                            ""AudiobookBooksToMonitor"" TEXT,
                            ""AudiobookBooksToSearch"" TEXT,
                            ""AudiobookTags"" TEXT,
                            ""EbookMonitored"" INTEGER,
                            ""EbookMonitorNewItems"" INTEGER,
                            ""EbookMonitorExistingMode"" INTEGER,
                            ""EbookQualityProfileId"" INTEGER,
                            ""EbookMetadataProfileId"" INTEGER,
                            ""EbookRootFolderPath"" TEXT,
                            ""EbookBooksToMonitor"" TEXT,
                            ""EbookBooksToSearch"" TEXT,
                            ""EbookTags"" TEXT,
                            ""Tags"" TEXT,
                            ""SearchForMissingBooks"" INTEGER,
                            ""LastSelectedMediaType"" TEXT,
                            ""AttemptCount"" INTEGER,
                            ""MaxAttempts"" INTEGER,
                            ""LastAttemptAt"" DATETIME,
                            ""LastError"" TEXT,
                            ""UpdatedAt"" DATETIME,
                            ""NextAttemptAt"" DATETIME,
                            ""Version"" INTEGER,
                            ""CreatedAt"" DATETIME
                        );
                        INSERT INTO ""PendingAuthorImport""
                            (""Id"", ""ProviderId"", ""AudiobookStatus"", ""EbookStatus"", ""OverallStatus"", ""SearchForMissingBooks"", ""AttemptCount"", ""MaxAttempts"", ""NextAttemptAt"", ""Version"", ""CreatedAt"")
                        VALUES
                            (1, 'gr:1', @Pending, @NotRequested, @Pending, 0, 0, 0, @Now, 4, @Now);",
                        new
                        {
                            Pending = PendingImportStatus.Pending,
                            NotRequested = PendingImportStatus.NotRequested,
                            Now = DateTime.UtcNow
                        });
                }

                var database = new Database("main", () =>
                {
                    var connection = new SqliteConnection(connectionString);
                    connection.Open();
                    return connection;
                });
                action(new PendingAuthorImportRepository(new MainDatabase(database), new StubEventAggregator()), connectionString);
            }
            finally
            {
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                }
            }
        }

        private static void WithRepository(Action<PendingAuthorImportRepository, string> action)
        {
            var databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"pending_author_retry_{Guid.NewGuid():N}.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    connection.Execute(@"
                        CREATE TABLE ""PendingAuthorImport"" (
                            ""Id"" INTEGER PRIMARY KEY,
                            ""ProviderId"" TEXT,
                            ""RequestKey"" TEXT,
                            ""SingleWorkOnly"" INTEGER,
                            ""SingleWorkRequests"" TEXT,
                            ""LockedBy"" TEXT,
                            ""LockedAt"" DATETIME,
                            ""LeaseExpiresAt"" DATETIME,
                            ""NextAttemptAt"" DATETIME,
                            ""OverallStatus"" INTEGER,
                            ""AttemptCount"" INTEGER,
                            ""MaxAttempts"" INTEGER,
                            ""CreatedAt"" DATETIME
                        );

                        INSERT INTO ""PendingAuthorImport""
                            (""Id"", ""ProviderId"", ""NextAttemptAt"", ""OverallStatus"", ""AttemptCount"", ""MaxAttempts"", ""CreatedAt"")
                        VALUES
                            (1, 'gr:1', @DueFirst, @Retrying, 100, 100, @Created),
                            (2, 'gr:2', @Future, @Retrying, 2, 100, @Created),
                            (3, 'gr:3', @DueSecond, @Failed, 100, 100, @Created),
                            (4, 'gr:4', @DueSecond, @Pending, 500, 1, @Created);",
                        new
                        {
                            DueFirst = DateTime.UtcNow.AddMinutes(-2),
                            DueSecond = DateTime.UtcNow.AddMinutes(-1),
                            Future = DateTime.UtcNow.AddMinutes(5),
                            Created = DateTime.UtcNow.AddDays(-1),
                            Retrying = PendingImportStatus.Retrying,
                            Pending = PendingImportStatus.Pending,
                            Failed = PendingImportStatus.Failed
                        });
                }

                var database = new Database("main", () =>
                {
                    var connection = new SqliteConnection(connectionString);
                    connection.Open();
                    return connection;
                });

                action(new PendingAuthorImportRepository(new MainDatabase(database), new StubEventAggregator()), connectionString);
            }
            finally
            {
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                }
            }
        }

        private sealed class TagProjection
        {
            public string AudiobookTags { get; set; }
            public string EbookTags { get; set; }
            public string Tags { get; set; }
            public string LastSelectedMediaType { get; set; }
        }
    }
}

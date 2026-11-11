using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using NLog;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Http;
using NzbDrone.Core.Messaging.Events;

namespace Chaptarr.Core.Test.Http
{
    [TestFixture]
    public class CachedHttpResponseRepositoryFixture
    {
        [OneTimeSetUp]
        public void RegisterMappings()
        {
            if (!TableMapping.Mapper.TableMap.ContainsKey(typeof(CachedHttpResponse)))
            {
                TableMapping.Map();
            }
        }

        private sealed class Events : IEventAggregator
        {
            public void PublishEvent<TEvent>(TEvent value) where TEvent : class, IEvent { }
        }

        private static CachedHttpResponse Response(string value, DateTime refreshed) => new CachedHttpResponse
        {
            Url = "https://cache.example.test/work/gr:1",
            LastRefresh = refreshed,
            Expiry = refreshed.AddHours(1),
            Value = value,
            StatusCode = 200
        };

        private static void Migrate(string connectionString, bool postgres, long? version = null) =>
            new MigrationController(LogManager.GetCurrentClassLogger(), null).Migrate(connectionString,
                new MigrationContext(MigrationType.Cache, version), postgres ? DatabaseType.PostgreSQL : DatabaseType.SQLite);

        private static void WithDatabase(bool postgres, Action<ICacheDatabase, string> test, long? version = null)
        {
            var adminString = Environment.GetEnvironmentVariable("CHAPTARR_TEST_POSTGRES_CONNECTION_STRING");
            if (postgres && string.IsNullOrEmpty(adminString))
            {
                Assert.Ignore("Set CHAPTARR_TEST_POSTGRES_CONNECTION_STRING to exercise PostgreSQL persistence.");
            }

            var name = "http_cache_" + Guid.NewGuid().ToString("N");
            var filename = Path.Combine(TestContext.CurrentContext.WorkDirectory, name + ".db");
            var connectionString = postgres
                ? new NpgsqlConnectionStringBuilder(adminString) { Database = name, Pooling = false }.ToString()
                : new SqliteConnectionStringBuilder { DataSource = filename, Pooling = false }.ToString();
            if (postgres)
            {
                using var admin = new NpgsqlConnection(adminString);
                admin.Open();
                admin.Execute($"CREATE DATABASE \"{name}\"");
            }

            IDbConnection Open()
            {
                IDbConnection connection = postgres ? new NpgsqlConnection(connectionString) : new SqliteConnection(connectionString);
                connection.Open();
                if (!postgres) connection.Execute("PRAGMA busy_timeout = 10000;");
                return connection;
            }

            try
            {
                Migrate(connectionString, postgres, version);
                test(new CacheDatabase(new Database(name, Open)), connectionString);
            }
            finally
            {
                if (postgres)
                {
                    using var admin = new NpgsqlConnection(adminString);
                    admin.Open();
                    admin.Execute($"DROP DATABASE IF EXISTS \"{name}\"");
                }
                else if (File.Exists(filename)) File.Delete(filename);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void migration_retains_newest_response_and_enforces_unique_urls(bool postgres)
        {
            WithDatabase(postgres, (database, connectionString) =>
            {
                var now = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
                using (var connection = database.OpenConnection())
                {
                    foreach (var response in new[] { Response("old", now.AddHours(-1)), Response("new", now), Response("tie-winner", now) })
                    {
                        connection.Execute(@"INSERT INTO ""HttpResponse"" (""Url"", ""LastRefresh"", ""Expiry"", ""Value"", ""StatusCode"")
                            VALUES (@Url, @LastRefresh, @Expiry, @Value, @StatusCode);", response);
                    }
                    connection.Execute(@"INSERT INTO ""HttpResponse"" (""Url"", ""LastRefresh"", ""Expiry"", ""Value"", ""StatusCode"")
                        VALUES ('https://cache.example.test/other', @LastRefresh, @Expiry, 'unrelated', 200);", Response("", now));
                }

                Migrate(connectionString, postgres);
                Migrate(connectionString, postgres); // Opening an upgraded cache again is safe.
                var repository = new CachedHttpResponseRepository(database, new Events());
                Assert.That(repository.FindByUrl(Response("", now).Url).Value, Is.EqualTo("tie-winner"));
                Assert.That(repository.FindByUrl(Response("", now).Url).LastRefresh, Is.EqualTo(now));
                Assert.That(repository.FindByUrl(Response("", now).Url).Expiry, Is.EqualTo(now.AddHours(1)));
                using var check = database.OpenConnection();
                Assert.That(check.ExecuteScalar<int>("SELECT COUNT(*) FROM \"HttpResponse\""), Is.EqualTo(2));
                Assert.That(() => check.Execute(@"INSERT INTO ""HttpResponse"" (""Url"", ""LastRefresh"", ""Expiry"", ""Value"", ""StatusCode"")
                    VALUES (@Url, @LastRefresh, @Expiry, @Value, @StatusCode);", Response("duplicate", now)), Throws.Exception);
            }, version: 14);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void concurrent_cold_writers_share_one_url_and_keep_the_newest_payload(bool postgres)
        {
            WithDatabase(postgres, (database, _) =>
            {
                var now = DateTime.UtcNow;
                using var barrier = new Barrier(8);
                var writes = Enumerable.Range(0, 8).Select(index => Task.Factory.StartNew(() =>
                {
                    var repository = new CachedHttpResponseRepository(database, new Events());
                    Assert.That(repository.FindByUrl(Response("", now).Url), Is.Null);
                    Assert.That(barrier.SignalAndWait(TimeSpan.FromSeconds(10)), Is.True);
                    repository.StoreByUrl(Response("payload-" + index, now.AddSeconds(index)));
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                Assert.That(Task.WaitAll(writes, TimeSpan.FromSeconds(30)), Is.True);
                var saved = new CachedHttpResponseRepository(database, new Events()).FindByUrl(Response("", now).Url);
                Assert.That(saved.Value, Is.EqualTo("payload-7"));
                using var check = database.OpenConnection();
                Assert.That(check.ExecuteScalar<int>("SELECT COUNT(*) FROM \"HttpResponse\""), Is.EqualTo(1));
            });
        }
    }
}

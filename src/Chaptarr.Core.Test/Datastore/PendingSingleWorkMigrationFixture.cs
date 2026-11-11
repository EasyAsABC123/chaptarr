using System;
using System.Data;
using System.Data.Common;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using NLog;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace Chaptarr.Core.Test.Datastore
{
    [TestFixture]
    public class PendingSingleWorkMigrationFixture
    {
        [TestCase(false)]
        [TestCase(true)]
        public void migration_preserves_legacy_author_uniqueness_and_isolates_single_work_identity(bool postgres)
        {
            var adminString = Environment.GetEnvironmentVariable("CHAPTARR_TEST_POSTGRES_CONNECTION_STRING");
            if (postgres && string.IsNullOrEmpty(adminString)) Assert.Ignore("Set CHAPTARR_TEST_POSTGRES_CONNECTION_STRING for PostgreSQL migration coverage.");
            var name = "pending_single_" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, name + ".db");
            var cs = postgres ? new NpgsqlConnectionStringBuilder(adminString) { Database = name, Pooling = false }.ToString()
                : new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
            if (postgres) { using var admin = new NpgsqlConnection(adminString); admin.Open(); admin.Execute($"CREATE DATABASE \"{name}\""); }
            try
            {
                using IDbConnection db = postgres ? new NpgsqlConnection(cs) : new SqliteConnection(cs); db.Open();
                db.Execute(@"CREATE TABLE ""VersionInfo"" (""Version"" BIGINT PRIMARY KEY, ""AppliedOn"" TEXT, ""Description"" TEXT);
                    WITH RECURSIVE versions(version) AS (SELECT 1 UNION ALL SELECT version+1 FROM versions WHERE version<129)
                    INSERT INTO ""VersionInfo"" SELECT version,CAST(CURRENT_TIMESTAMP AS VARCHAR(255)),'baseline' FROM versions;
                    CREATE TABLE ""PendingAuthorImport"" (""Id"" INTEGER PRIMARY KEY,""ProviderId"" TEXT,""OverallStatus"" INTEGER);
                    CREATE UNIQUE INDEX ""UX_PendingAuthorImport_Active"" ON ""PendingAuthorImport""(""ProviderId"") WHERE ""OverallStatus"" IN (1,2,3);
                    INSERT INTO ""PendingAuthorImport"" VALUES(1,'gr:7',1);");
                new MigrationController(LogManager.GetCurrentClassLogger(), null).Migrate(cs, new MigrationContext(MigrationType.Main, 130), postgres ? DatabaseType.PostgreSQL : DatabaseType.SQLite);
                Assert.That(db.ExecuteScalar<string>("SELECT \"RequestKey\" FROM \"PendingAuthorImport\" WHERE \"Id\"=1"), Is.EqualTo("gr:7"));
                Assert.That(db.ExecuteScalar<bool>("SELECT \"SingleWorkOnly\" FROM \"PendingAuthorImport\" WHERE \"Id\"=1"), Is.False);
                Assert.Catch<DbException>(() => db.Execute("INSERT INTO \"PendingAuthorImport\"(\"Id\",\"ProviderId\",\"OverallStatus\") VALUES(2,'gr:7',1)"));
                db.Execute(@"INSERT INTO ""PendingAuthorImport""(""Id"",""ProviderId"",""OverallStatus"",""SingleWorkOnly"",""RequestKey"") VALUES(2,'gr:7',1,@Mode,'exact-a'),(3,'gr:7',1,@Mode,'exact-b')", new { Mode = true });
                Assert.Catch<DbException>(() => db.Execute(@"INSERT INTO ""PendingAuthorImport""(""Id"",""ProviderId"",""OverallStatus"",""SingleWorkOnly"",""RequestKey"") VALUES(4,'gr:7',1,@Mode,'exact-a')", new { Mode = true }));
                db.Execute(@"UPDATE ""PendingAuthorImport"" SET ""OverallStatus""=6 WHERE ""Id""=2;
                    INSERT INTO ""PendingAuthorImport""(""Id"",""ProviderId"",""OverallStatus"",""SingleWorkOnly"",""RequestKey"") VALUES(4,'gr:7',1,@Mode,'exact-a')", new { Mode = true });
                var payload = new string('x', 8192);
                db.Execute("UPDATE \"PendingAuthorImport\" SET \"SingleWorkRequests\"=@Payload WHERE \"Id\"=4", new { Payload = payload });
                Assert.That(db.ExecuteScalar<string>("SELECT \"SingleWorkRequests\" FROM \"PendingAuthorImport\" WHERE \"Id\"=4"), Is.EqualTo(payload));
                Assert.That(db.ExecuteScalar<int>("SELECT COUNT(*) FROM \"PendingAuthorImport\""), Is.EqualTo(4));
            }
            finally
            {
                if (postgres) { using var admin = new NpgsqlConnection(adminString); admin.Open(); admin.Execute($"DROP DATABASE IF EXISTS \"{name}\""); }
                else File.Delete(path);
            }
        }
    }
}

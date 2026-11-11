using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(129)]
    public class unique_http_response_cache_urls : NzbDroneMigrationBase
    {
        protected override void CacheDbUpgrade()
        {
            // Retain the freshest response, resolving equal refresh times by Id.
            // FluentMigrator runs this repair and the unique index in one transaction.
            Execute.Sql(@"
                DELETE FROM ""HttpResponse""
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT ""Id"", ROW_NUMBER() OVER (
                            PARTITION BY ""Url"" ORDER BY ""LastRefresh"" DESC, ""Id"" DESC
                        ) AS ""DuplicateRank""
                        FROM ""HttpResponse""
                    ) duplicates
                    WHERE ""DuplicateRank"" > 1
                );");

            Delete.Index("IX_HttpResponse_Url").OnTable("HttpResponse");
            Create.Index("IX_HttpResponse_Url").OnTable("HttpResponse").OnColumn("Url").Unique();
        }
    }
}

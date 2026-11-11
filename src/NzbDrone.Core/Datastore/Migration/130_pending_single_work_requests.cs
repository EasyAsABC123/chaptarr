using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(130)]
    public class pending_single_work_requests : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("PendingAuthorImport").AddColumn("RequestKey").AsString().Nullable();
            Alter.Table("PendingAuthorImport").AddColumn("SingleWorkOnly").AsBoolean().NotNullable().WithDefaultValue(false);
            Alter.Table("PendingAuthorImport").AddColumn("SingleWorkRequests").AsString(int.MaxValue).Nullable();
            Execute.Sql("UPDATE \"PendingAuthorImport\" SET \"RequestKey\" = \"ProviderId\"");
            Delete.Index("UX_PendingAuthorImport_Active").OnTable("PendingAuthorImport");
            Execute.Sql(@"CREATE UNIQUE INDEX ""UX_PendingAuthorImport_Active""
                ON ""PendingAuthorImport"" (""ProviderId"")
                WHERE ""OverallStatus"" IN (1,2,3) AND NOT ""SingleWorkOnly"";
                CREATE UNIQUE INDEX ""UX_PendingAuthorImport_SingleWork""
                ON ""PendingAuthorImport"" (""RequestKey"")
                WHERE ""OverallStatus"" IN (1,2,3) AND ""SingleWorkOnly"";");
        }
    }
}

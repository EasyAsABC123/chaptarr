using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Http
{
    public interface ICachedHttpResponseRepository : IBasicRepository<CachedHttpResponse>
    {
        CachedHttpResponse FindByUrl(string url);
        void StoreByUrl(CachedHttpResponse response);
    }

    public class CachedHttpResponseRepository : BasicRepository<CachedHttpResponse>, ICachedHttpResponseRepository
    {
        public CachedHttpResponseRepository(ICacheDatabase database,
                                            IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public void StoreByUrl(CachedHttpResponse response)
        {
            using var connection = _database.OpenConnection();
            connection.Execute(@"
                INSERT INTO ""HttpResponse"" (""Url"", ""LastRefresh"", ""Expiry"", ""Value"", ""StatusCode"")
                VALUES (@Url, @LastRefresh, @Expiry, @Value, @StatusCode)
                ON CONFLICT(""Url"") DO UPDATE SET
                    ""LastRefresh"" = excluded.""LastRefresh"",
                    ""Expiry"" = excluded.""Expiry"",
                    ""Value"" = excluded.""Value"",
                    ""StatusCode"" = excluded.""StatusCode""
                WHERE excluded.""LastRefresh"" >= ""HttpResponse"".""LastRefresh"";", response);
        }

        public CachedHttpResponse FindByUrl(string url)
        {
            var edition = Query(x => x.Url == url).SingleOrDefault();

            return edition;
        }
    }
}

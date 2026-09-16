using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.SeriesWatch
{
    public interface ISeriesWatchCatalog
    {
        List<ImportListItemInfo> Fetch(string providerId, bool includeExtras);
    }

    // Only Chaptarr talks to metadata providers; SeerrNG never needs their credentials.
    public class SeriesWatchCatalog : ISeriesWatchCatalog
    {
        private readonly IHttpClient _http;
        private readonly IConfigService _config;
        private readonly IProvideSeriesInfo _goodreads;

        public SeriesWatchCatalog(IHttpClient http, IConfigService config, IProvideSeriesInfo goodreads)
        {
            _http = http;
            _config = config;
            _goodreads = goodreads;
        }

        public List<ImportListItemInfo> Fetch(string providerId, bool includeExtras)
        {
            if (!Regex.IsMatch(providerId ?? "", @"^(hc|gr):[1-9]\d*$") ||
                !int.TryParse(providerId.Substring(3), out var id))
            {
                throw new ArgumentException("An exact supported series provider ID is required.");
            }

            if (providerId.StartsWith("gr:"))
            {
                if (!includeExtras) throw new ArgumentException("Goodreads requires including extras.");
                return _goodreads.GetSeriesInfo(id).Works.Select(work => new ImportListItemInfo
                {
                    BookProviderId = $"gr:{work.Id}",
                    Book = work.OriginalTitle,
                    AuthorProviderId = $"gr:{work.BestBook.AuthorId}",
                    Author = work.BestBook.AuthorName
                }).ToList();
            }

            if (!_config.HardcoverEnabled || string.IsNullOrWhiteSpace(_config.HardcoverApiToken))
                throw new InvalidOperationException("Configure Hardcover in Chaptarr before watching this catalog.");

            var result = new List<ImportListItemInfo>();
            const int pageSize = 100;
            // Refuse a truncated fetch: an empty or partial list must not look like a successful watch.
            for (var offset = 0; offset <= 5000; offset += pageSize)
            {
                var query = @"query SeriesWatch($id: Int!, $offset: Int!, $limit: Int!) {
                  series_by_pk(id: $id) { id book_series(WHERE_CLAUSE order_by: {id: asc}, offset: $offset, limit: $limit) {
                    book { id title contributions(where: {_or: [{contribution: {_eq: ""Author""}}, {contribution: {_eq: """"}}, {contribution: {_is_null: true}}]}, order_by: {id: asc}, limit: 1) { author { id name } } }
                  } }
                }".Replace("WHERE_CLAUSE", includeExtras ? "" : "where: {featured: {_eq: true}},");
                var request = new HttpRequestBuilder("https://api.hardcover.app/v1/graphql").Build();
                request.Method = HttpMethod.Post;
                request.Headers.Add("Content-Type", "application/json");
                var token = _config.HardcoverApiToken.Trim();
                request.Headers.Add("Authorization", token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : $"Bearer {token}");
                request.SetContent(JsonSerializer.Serialize(new { query, variables = new { id, offset, limit = pageSize } }));
                var response = _http.Execute(request);
                if (response == null || response.HasHttpError) throw new InvalidOperationException("Series catalog unavailable.");
                using var document = JsonDocument.Parse(response.Content);
                var root = document.RootElement;
                if (root.TryGetProperty("errors", out _) || !root.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("series_by_pk", out var series) || series.ValueKind != JsonValueKind.Object ||
                    series.GetProperty("id").GetInt32() != id)
                    throw new InvalidOperationException("Series catalog unavailable or missing.");
                var rows = series.GetProperty("book_series");
                if (offset == 5000 && rows.GetArrayLength() > 0) throw new InvalidOperationException("Series exceeds the supported catalog size.");
                foreach (var row in rows.EnumerateArray())
                {
                    var book = row.GetProperty("book");
                    var contributions = book.GetProperty("contributions");
                    if (contributions.GetArrayLength() == 0) throw new InvalidOperationException("Series book has no verified author.");
                    var author = contributions[0].GetProperty("author");
                    result.Add(new ImportListItemInfo
                    {
                        BookProviderId = $"hc:{book.GetProperty("id").GetInt32()}",
                        Book = book.GetProperty("title").GetString(),
                        AuthorProviderId = $"hc:{author.GetProperty("id").GetInt32()}",
                        Author = author.GetProperty("name").GetString()
                    });
                }

                if (rows.GetArrayLength() < pageSize) return result.DistinctBy(item => item.BookProviderId).ToList();
            }

            throw new InvalidOperationException("Series catalog did not finish.");
        }
    }
}

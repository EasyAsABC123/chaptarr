using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Chaptarr.Api.V1.Series
{
    // Identifier evidence is scoped to the exact book in the series response.
    // A sentinel edition reveals truncation so callers cannot mistake a subset for complete evidence.
    public class SeriesBookIdentifiers
    {
        public List<string> Isbns { get; set; } = new List<string>();
        public bool Complete { get; set; }

        public static SeriesBookIdentifiers Read(JsonElement book)
        {
            var result = new SeriesBookIdentifiers();
            if (!book.TryGetProperty("editions", out var editions) || editions.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            result.Complete = editions.GetArrayLength() <= 200;
            var isbns = new HashSet<string>();
            foreach (var edition in editions.EnumerateArray().Take(200))
            {
                if (edition.ValueKind != JsonValueKind.Object)
                {
                    result.Complete = false;
                    continue;
                }

                foreach (var field in new[] { "isbn_13", "isbn_10" })
                {
                    if (!edition.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

                    if (value.ValueKind != JsonValueKind.String)
                    {
                        result.Complete = false;
                        continue;
                    }

                    var isbn = value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(isbn)) isbns.Add(isbn);
                }
            }

            result.Isbns = isbns.OrderBy(value => value).ToList();
            return result;
        }
    }
}

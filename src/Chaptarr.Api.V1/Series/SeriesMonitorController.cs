using System.Linq;
using Chaptarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;

namespace Chaptarr.Api.V1.Series
{
    [V1ApiController("series")]
    public class SeriesMonitorController : Controller
    {
        private readonly ISeriesService _seriesService;
        private readonly ISeriesBookLinkService _links;
        private readonly IBookService _books;

        public SeriesMonitorController(ISeriesService seriesService, ISeriesBookLinkService links, IBookService books)
        {
            _seriesService = seriesService;
            _links = links;
            _books = books;
        }

        [HttpPut("{seriesId:int}/monitor")]
        public IActionResult Monitor(int seriesId, [FromBody] SeriesMonitorResource resource)
        {
            if (resource?.Monitored == null || resource.BookIds == null ||
                resource.BookIds.Length == 0 || resource.BookIds.Length > 500 || resource.BookIds.Any(id => id <= 0))
            {
                return BadRequest(new { message = "Specify monitored and between 1 and 500 positive bookIds." });
            }

            var series = _seriesService.GetSeries(seriesId);
            if (series == null)
            {
                return NotFound();
            }

            if (series.MediaType != BookMediaType.Ebook && series.MediaType != BookMediaType.Audiobook)
            {
                return Conflict(new { message = "This series has an unsupported media type." });
            }

            var ids = resource.BookIds.Distinct().ToArray();
            var members = _links.GetLinksBySeries(seriesId).Select(link => link.BookId).ToHashSet();
            if (ids.Any(id => !members.Contains(id)))
            {
                return BadRequest(new { message = "Every selected book must belong to this series." });
            }

            var books = _books.GetExistingBooks(ids);
            if (books.Count != ids.Length || books.Any(book => book.MediaType != series.MediaType))
            {
                return Conflict(new { message = "Series membership has changed or contains a different format. Refresh before retrying." });
            }

            var mediaType = series.MediaType == BookMediaType.Ebook ? "ebook" : "audiobook";
            // The general book monitor operation can synchronize companion formats.
            // Series intent is explicitly scoped; leave author and import-list policy intact.
            _books.SetMonitoredForMediaType(ids, mediaType, resource.Monitored.Value);
            var saved = _books.GetExistingBooks(ids).ToDictionary(book => book.Id);
            var blockedIds = ids.Where(id => !saved.TryGetValue(id, out var book) ||
                book.IsMonitoredForMediaType(mediaType) != resource.Monitored.Value).ToArray();
            if (blockedIds.Length > 0)
            {
                return Conflict(new
                {
                    message = "Some books could not be updated under the current author monitoring settings. Refresh to see the saved state.",
                    bookIds = blockedIds
                });
            }

            return Ok(new { bookIds = ids, monitored = resource.Monitored.Value, mediaType });
        }
    }

    public class SeriesMonitorResource
    {
        public int[] BookIds { get; set; }
        public bool? Monitored { get; set; }
    }
}

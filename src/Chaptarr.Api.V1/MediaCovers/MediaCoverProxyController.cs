using System.Text.RegularExpressions;
using Chaptarr.Http;
using Chaptarr.Http.Frontend.Mappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.MediaCover;

namespace Chaptarr.Api.V1.MediaCovers
{
    [V1ApiController]
    [Authorize(AuthenticationSchemes = "API")]
    public class MediaCoverProxyController : Controller
    {
        private readonly MediaCoverProxyMapper _mapper;

        public MediaCoverProxyController(IMediaCoverProxy mediaCoverProxy, Logger logger)
        {
            _mapper = new MediaCoverProxyMapper(mediaCoverProxy, logger);
        }

        [HttpGet("{hash}/{filename}")]
        public IActionResult GetCover(string hash, string filename)
        {
            if (hash == null || filename == null ||
                !Regex.IsMatch(hash, @"\A[a-fA-F0-9]{64}\z") ||
                !Regex.IsMatch(filename, @"\A[A-Za-z0-9_.-]+\.(jpg|jpeg|png|webp|gif|avif)\z", RegexOptions.IgnoreCase))
            {
                return NotFound();
            }

            return _mapper.GetResponse($"/MediaCoverProxy/{hash}/{filename}");
        }
    }
}

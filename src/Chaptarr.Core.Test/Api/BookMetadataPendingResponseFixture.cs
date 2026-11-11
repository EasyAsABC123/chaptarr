using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Chaptarr.Http.ErrorManagement;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace Chaptarr.Core.Test.Api
{
    [TestFixture]
    public class BookMetadataPendingResponseFixture
    {
        [Test]
        public async Task should_return_retryable_machine_readable_pending_response()
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
            {
                Error = new BookMetadataPendingException("gr:3046572", TimeSpan.FromSeconds(60)),
                Path = "/api/v1/book"
            });
            await new ChaptarrErrorPipeline(LogManager.GetCurrentClassLogger()).HandleException(context);
            context.Response.Body.Position = 0;
            using var body = await JsonDocument.ParseAsync(context.Response.Body);
            Assert.That(context.Response.StatusCode, Is.EqualTo(503));
            Assert.That(context.Response.Headers["Retry-After"].ToString(), Is.EqualTo("60"));
            Assert.That(body.RootElement.GetProperty("code").GetString(), Is.EqualTo("metadata_pending"));
            Assert.That(body.RootElement.GetProperty("retryAfterSeconds").GetInt32(), Is.EqualTo(60));
        }
        [TestCase(false)]
        [TestCase(true)]
        public async Task should_return_metadata_unavailable_for_outages_and_invalid_provider_payloads(bool malformed)
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
            {
                Error = malformed ? new BookInfoException("Invalid JSON from metadata provider") : new MetadataServerUnavailableException("work lookup", TimeSpan.FromSeconds(120)),
                Path = "/api/v1/book"
            });
            await new ChaptarrErrorPipeline(LogManager.GetCurrentClassLogger()).HandleException(context);
            context.Response.Body.Position = 0;
            using var body = await JsonDocument.ParseAsync(context.Response.Body);
            Assert.That(context.Response.StatusCode, Is.EqualTo(503));
            Assert.That(context.Response.Headers["Retry-After"].ToString(), Is.EqualTo(malformed ? "60" : "120"));
            Assert.That(body.RootElement.GetProperty("code").GetString(), Is.EqualTo("metadata_unavailable"));
            Assert.That(body.RootElement.GetProperty("retryAfterSeconds").GetInt32(), Is.EqualTo(malformed ? 60 : 120));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task should_keep_missing_and_invalid_identities_distinct_from_service_unavailability(bool invalid)
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
            {
                Error = invalid ? new InvalidProviderIdException("123", "Provider prefix required") : new BookNotFoundException("gr:3046572"),
                Path = "/api/v1/book"
            });
            await new ChaptarrErrorPipeline(LogManager.GetCurrentClassLogger()).HandleException(context);
            Assert.That(context.Response.StatusCode, Is.EqualTo(invalid ? 400 : 404));
            Assert.That(context.Response.Headers.ContainsKey("Retry-After"), Is.False);
        }
    }
}

using System;
using System.Net;

namespace NzbDrone.Core.Exceptions
{
    public class BookMetadataPendingException : NzbDroneClientException
    {
        public string ProviderId { get; }
        public int RetryAfterSeconds { get; }

        public BookMetadataPendingException(string providerId, TimeSpan? retryAfter = null)
            : base(HttpStatusCode.ServiceUnavailable, $"Metadata for book {providerId} is still being prepared. Retry when the metadata server is ready.")
        {
            ProviderId = providerId;
            RetryAfterSeconds = (int)Math.Clamp(Math.Ceiling((retryAfter ?? TimeSpan.FromSeconds(60)).TotalSeconds), 1, 900);
        }
    }
}

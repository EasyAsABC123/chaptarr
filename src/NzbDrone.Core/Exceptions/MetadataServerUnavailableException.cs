using System;
using System.Net;

namespace NzbDrone.Core.Exceptions
{
    public class MetadataServerUnavailableException : NzbDroneClientException
    {
        public int RetryAfterSeconds { get; }

        public MetadataServerUnavailableException(string operation, TimeSpan? retryAfter = null, Exception innerException = null)
            : base(HttpStatusCode.ServiceUnavailable,
                $"Metadata server is temporarily unavailable for {operation}. Retry when the metadata service is ready.", innerException)
        {
            RetryAfterSeconds = (int)Math.Clamp(Math.Ceiling((retryAfter ?? TimeSpan.FromSeconds(60)).TotalSeconds), 1, 900);
        }
    }
}

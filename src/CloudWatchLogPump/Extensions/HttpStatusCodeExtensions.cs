using System.Net;

namespace CloudWatchLogPump.Extensions
{
    public static class HttpStatusCodeExtensions
    {
        public static bool IsRetryable(this HttpStatusCode code)
        {
            return
                code == HttpStatusCode.Conflict ||
                code == HttpStatusCode.Locked ||
                code == HttpStatusCode.TooManyRequests ||
                code == HttpStatusCode.InternalServerError ||
                code == HttpStatusCode.BadGateway ||
                code == HttpStatusCode.ServiceUnavailable ||
                code == HttpStatusCode.GatewayTimeout;
        }
    }
}
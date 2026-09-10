using System.Net;

namespace FleetGo.Shared.Http;

/// <summary>
/// Thrown when the FleetGo API is reachable but answers with something the
/// client cannot use. Transport failures surface as <see cref="HttpRequestException"/>
/// and are deliberately left alone so callers can tell "server said no" apart
/// from "there is no network".
/// </summary>
public sealed class FleetGoApiException : Exception
{
    public FleetGoApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status returned by the API, when there was a response at all.</summary>
    public HttpStatusCode? StatusCode { get; }
}

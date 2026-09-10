using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Serialization;

namespace FleetGo.Shared.Http;

/// <summary>
/// Default <see cref="IFleetGoApiClient"/> implementation.
/// <para>
/// It takes an <see cref="HttpClient"/> and nothing else: base address, timeouts,
/// default headers and retry policies are the composition root's job (registered
/// through <c>AddHttpClient</c>), which keeps this class free of configuration
/// concerns and trivial to test with a stubbed message handler.
/// </para>
/// </summary>
public sealed class FleetGoApiClient : IFleetGoApiClient
{
    private readonly HttpClient _httpClient;

    public FleetGoApiClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public Task<ApiInfoResponse> GetApiInfoAsync(CancellationToken cancellationToken = default) =>
        GetAsync(ApiRoutes.SystemInfo, FleetGoJsonSerializerContext.Default.ApiInfoResponse, cancellationToken);

    /// <inheritdoc />
    public Task<HealthReportResponse> GetHealthAsync(CancellationToken cancellationToken = default) =>
        // A degraded API answers 503 with a perfectly readable report - that is a
        // successful call from the client's point of view, not a failure.
        GetAsync(
            ApiRoutes.Health,
            FleetGoJsonSerializerContext.Default.HealthReportResponse,
            cancellationToken,
            HttpStatusCode.ServiceUnavailable);

    private async Task<T> GetAsync<T>(
        string route,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken,
        params HttpStatusCode[] alsoAcceptable)
        where T : class
    {
        using HttpResponseMessage response = await _httpClient
            .GetAsync(route, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode && Array.IndexOf(alsoAcceptable, response.StatusCode) < 0)
        {
            throw new FleetGoApiException(
                $"GET {route} failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                response.StatusCode);
        }

        try
        {
            T? payload = await response.Content
                .ReadFromJsonAsync(typeInfo, cancellationToken)
                .ConfigureAwait(false);

            return payload ?? throw new FleetGoApiException(
                $"GET {route} returned an empty body.",
                response.StatusCode);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new FleetGoApiException(
                $"GET {route} returned a body that could not be read as {typeof(T).Name}.",
                response.StatusCode,
                ex);
        }
    }
}

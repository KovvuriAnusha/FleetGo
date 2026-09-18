using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Auth;
using FleetGo.Shared.Contracts.Fleet;
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

    /// <inheritdoc />
    public Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        PostAsync(
            ApiRoutes.AuthLogin,
            request,
            FleetGoJsonSerializerContext.Default.LoginRequest,
            FleetGoJsonSerializerContext.Default.TokenResponse,
            cancellationToken,
            // There is no session yet to attach a token from, and this call must never
            // itself trigger IAccessTokenProvider's own refresh logic.
            skipAuthentication: true);

    /// <inheritdoc />
    public Task<TokenResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default) =>
        PostAsync(
            ApiRoutes.AuthRefresh,
            request,
            FleetGoJsonSerializerContext.Default.RefreshTokenRequest,
            FleetGoJsonSerializerContext.Default.TokenResponse,
            cancellationToken,
            // Refreshing is how a new access token is obtained - it cannot depend on one
            // already being present, or a token provider that refreshes-on-demand would
            // call straight back into this method.
            skipAuthentication: true);

    /// <inheritdoc />
    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, ApiRoutes.AuthLogout)
        {
            Content = JsonContent.Create(request, FleetGoJsonSerializerContext.Default.LogoutRequest),
        };

        using HttpResponseMessage response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new FleetGoApiException(
                $"POST {ApiRoutes.AuthLogout} failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                response.StatusCode);
        }
    }

    /// <inheritdoc />
    public Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        GetAsync(ApiRoutes.AuthMe, FleetGoJsonSerializerContext.Default.CurrentUserResponse, cancellationToken);

    /// <inheritdoc />
    public Task RequestOtpAsync(RequestOtpRequest request, CancellationToken cancellationToken = default) =>
        PostNoContentAsync(
            ApiRoutes.AuthOtpRequest,
            request,
            FleetGoJsonSerializerContext.Default.RequestOtpRequest,
            cancellationToken,
            // No session exists yet, and this call must never itself trigger a token refresh.
            skipAuthentication: true);

    /// <inheritdoc />
    public Task<TokenResponse> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken cancellationToken = default) =>
        PostAsync(
            ApiRoutes.AuthOtpVerify,
            request,
            FleetGoJsonSerializerContext.Default.VerifyOtpRequest,
            FleetGoJsonSerializerContext.Default.TokenResponse,
            cancellationToken,
            skipAuthentication: true);

    /// <inheritdoc />
    public Task<PagedResponse<RouteResponse>> GetRoutesAsync(
        int page = 1,
        int pageSize = 20,
        RouteStatus? status = null,
        DateOnly? routeDate = null,
        string? sort = null,
        CancellationToken cancellationToken = default) =>
        GetAsync(
            ApiRoutes.RoutesBase + BuildQuery(
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                ("status", status?.ToString()),
                ("routeDate", routeDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("sort", sort)),
            FleetGoJsonSerializerContext.Default.PagedResponseRouteResponse,
            cancellationToken);

    /// <inheritdoc />
    public Task<RouteResponse> GetRouteAsync(Guid routeId, CancellationToken cancellationToken = default) =>
        GetAsync(
            $"{ApiRoutes.RoutesBase}/{routeId}",
            FleetGoJsonSerializerContext.Default.RouteResponse,
            cancellationToken);

    /// <inheritdoc />
    public Task<PagedResponse<StopResponse>> GetStopsAsync(
        Guid? routeId = null,
        int page = 1,
        int pageSize = 20,
        StopStatus? status = null,
        string? search = null,
        string? sort = null,
        CancellationToken cancellationToken = default) =>
        GetAsync(
            ApiRoutes.StopsBase + BuildQuery(
                ("routeId", routeId?.ToString()),
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                ("status", status?.ToString()),
                ("search", search),
                ("sort", sort)),
            FleetGoJsonSerializerContext.Default.PagedResponseStopResponse,
            cancellationToken);

    /// <inheritdoc />
    public Task<StopResponse> GetStopAsync(Guid stopId, CancellationToken cancellationToken = default) =>
        GetAsync(
            $"{ApiRoutes.StopsBase}/{stopId}",
            FleetGoJsonSerializerContext.Default.StopResponse,
            cancellationToken);

    /// <inheritdoc />
    public Task<PagedResponse<PackageResponse>> GetPackagesAsync(
        Guid? stopId = null,
        int page = 1,
        int pageSize = 20,
        PackageStatus? status = null,
        string? search = null,
        CancellationToken cancellationToken = default) =>
        GetAsync(
            ApiRoutes.PackagesBase + BuildQuery(
                ("stopId", stopId?.ToString()),
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                ("status", status?.ToString()),
                ("search", search)),
            FleetGoJsonSerializerContext.Default.PagedResponsePackageResponse,
            cancellationToken);

    /// <inheritdoc />
    public Task<VehicleResponse> GetVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default) =>
        GetAsync(
            $"{ApiRoutes.VehiclesBase}/{vehicleId}",
            FleetGoJsonSerializerContext.Default.VehicleResponse,
            cancellationToken);

    /// <inheritdoc />
    public Task<CustomerResponse> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        GetAsync(
            $"{ApiRoutes.CustomersBase}/{customerId}",
            FleetGoJsonSerializerContext.Default.CustomerResponse,
            cancellationToken);

    /// <summary>
    /// Builds a query string from the parameters that actually have a value, skipping the
    /// rest. Values are escaped, so a search term with an ampersand or a space cannot break
    /// the URL (or smuggle in another parameter).
    /// </summary>
    private static string BuildQuery(params (string Key, string? Value)[] parameters)
    {
        var pairs = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
            .Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value!)}")
            .ToArray();

        return pairs.Length == 0 ? string.Empty : "?" + string.Join("&", pairs);
    }

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

        return await ReadBodyAsync("GET", route, response, typeInfo, cancellationToken, alsoAcceptable)
            .ConfigureAwait(false);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string route,
        TRequest body,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken,
        bool skipAuthentication = false,
        params HttpStatusCode[] alsoAcceptable)
        where TResponse : class
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(body, requestTypeInfo),
        };

        if (skipAuthentication)
        {
            httpRequest.Options.Set(FleetGoHttpRequestOptions.SkipAuthentication, true);
        }

        using HttpResponseMessage response = await _httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        return await ReadBodyAsync("POST", route, response, responseTypeInfo, cancellationToken, alsoAcceptable)
            .ConfigureAwait(false);
    }

    /// <summary>Like <see cref="PostAsync{TRequest, TResponse}"/>, for an endpoint whose success response has no body (204).</summary>
    private async Task PostNoContentAsync<TRequest>(
        string route,
        TRequest body,
        JsonTypeInfo<TRequest> requestTypeInfo,
        CancellationToken cancellationToken,
        bool skipAuthentication = false)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(body, requestTypeInfo),
        };

        if (skipAuthentication)
        {
            httpRequest.Options.Set(FleetGoHttpRequestOptions.SkipAuthentication, true);
        }

        using HttpResponseMessage response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new FleetGoApiException(
                $"POST {route} failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                response.StatusCode);
        }
    }

    private static async Task<T> ReadBodyAsync<T>(
        string method,
        string route,
        HttpResponseMessage response,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken,
        HttpStatusCode[] alsoAcceptable)
        where T : class
    {
        if (!response.IsSuccessStatusCode && Array.IndexOf(alsoAcceptable, response.StatusCode) < 0)
        {
            throw new FleetGoApiException(
                $"{method} {route} failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                response.StatusCode);
        }

        try
        {
            T? payload = await response.Content
                .ReadFromJsonAsync(typeInfo, cancellationToken)
                .ConfigureAwait(false);

            return payload ?? throw new FleetGoApiException(
                $"{method} {route} returned an empty body.",
                response.StatusCode);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new FleetGoApiException(
                $"{method} {route} returned a body that could not be read as {typeof(T).Name}.",
                response.StatusCode,
                ex);
        }
    }
}


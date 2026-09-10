using System.Net.Http.Headers;

namespace FleetGo.Shared.Http;

/// <summary>
/// Attaches <c>Authorization: Bearer &lt;token&gt;</c> to outgoing requests.
/// <para>
/// Registered as a message handler on the typed <see cref="FleetGoApiClient"/> (see
/// <c>AddHttpMessageHandler</c> in the mobile app's composition root), which is what lets
/// every call through <see cref="IFleetGoApiClient"/> reach an authenticated endpoint
/// without each call site remembering to attach a token itself.
/// </para>
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IAccessTokenProvider _accessTokenProvider;

    public BearerTokenHandler(IAccessTokenProvider accessTokenProvider)
    {
        ArgumentNullException.ThrowIfNull(accessTokenProvider);
        _accessTokenProvider = accessTokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(FleetGoHttpRequestOptions.SkipAuthentication, out bool skip) || !skip)
        {
            string? accessToken = await _accessTokenProvider
                .GetAccessTokenAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}


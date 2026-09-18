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
/// <para>
/// The token provider is supplied as a factory rather than as a constructed instance, and is
/// resolved per request instead of in the constructor. That is what keeps the composition
/// root acyclic: the session service that implements <see cref="IAccessTokenProvider"/> also
/// consumes <see cref="IFleetGoApiClient"/> (it has to call login/refresh), and
/// <c>IHttpClientFactory</c> builds this handler chain <em>while</em> that client is being
/// created. Resolving the provider in the constructor would therefore re-enter the
/// half-built session service, which is a circular construction the container cannot
/// satisfy. Deferring the lookup to <see cref="SendAsync"/> - by which point every singleton
/// is fully constructed - removes the cycle without changing what is sent on the wire.
/// </para>
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly Func<IAccessTokenProvider> _accessTokenProviderAccessor;

    /// <param name="accessTokenProviderAccessor">
    /// Returns the current <see cref="IAccessTokenProvider"/>. Invoked once per outgoing
    /// request that needs a token; in the app this resolves the session singleton from the
    /// container, so every call still sees the one session, never a copy of it.
    /// </param>
    public BearerTokenHandler(Func<IAccessTokenProvider> accessTokenProviderAccessor)
    {
        ArgumentNullException.ThrowIfNull(accessTokenProviderAccessor);
        _accessTokenProviderAccessor = accessTokenProviderAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(FleetGoHttpRequestOptions.SkipAuthentication, out bool skip) || !skip)
        {
            IAccessTokenProvider accessTokenProvider = _accessTokenProviderAccessor();

            string? accessToken = await accessTokenProvider
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


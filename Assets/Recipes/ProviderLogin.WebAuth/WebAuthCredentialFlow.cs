// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Auth.Addon.WebAuth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Opens provider authentication, validates the callback, and exchanges the code.
    /// <see cref="WebAuthOptions.ClientSideExchange"/> selects direct provider exchange.
    /// </summary>
    /// <remarks>
    /// Provider PKCE is created here for X and Google; Apple omits provider PKCE and uses server-side
    /// exchange. The Recipe's shared join creates a separate PKCE pair for the Axyl authorization
    /// code for every provider, including Apple. The provider and Axyl pairs must not be crossed.
    /// </remarks>
    internal static class WebAuthCredentialFlow
    {
        internal static async Task<ProviderCredentialOutcome> AcquireAsync(
            LoginProvider provider,
            Provider tokenProvider,
            WebAuthOptions options,
            CancellationToken cancellationToken,
            string relayCallbackUri = null)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!HiveCore.TryResolve<IAuthService>(out var auth) || auth == null)
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "IAuthService is not registered. Initialize the SDK with AddAuth first."));
            }

            if (!HiveCore.TryResolve<IExternalUserAgent>(out var agent) || agent == null)
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Unavailable,
                    "IExternalUserAgent is not registered, so no browser sign-in can run in this build."));
            }

            var apple = tokenProvider == Provider.SigninApple;
            if (apple && cancellationToken.IsCancellationRequested)
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Cancelled, "Apple browser sign-in was cancelled."));
            }

            // Two redirect URIs: where the app receives the result and where the provider sends it.
            //
            //   openUri      what OpenAsync is given: the app callback or the reserved loopback URI.
            //                On Windows the native agent requires the URI of its reserved listener.
            //   authorizeUri the redirect_uri inside the authorize URL, which is what the provider
            //                redirects to and what the token exchange must repeat.
            //
            // A provider that exact-matches its registered callback cannot be handed a loopback URI
            // whose port changes per run. The server callback takes the redirect instead and relays
            // it back to the listener, and the port rides along on the state as "<port>:" for the
            // relay to route by. Where the provider does accept a variable port, no relay is needed
            // and the loopback origin goes into the authorize URL directly.
            // Apple also uses distinct URIs outside Windows: its registered HTTPS relay receives
            // the provider response, then forwards it to the app callback carried in the state prefix.
            string openUri;
            string authorizeUri;
            var stateRoutingPrefix = string.Empty;

            // AddWebAuth exposes the loopback agent through the plugin, not as a separate service.
            // Fall back to a registered agent when the application supplies its own implementation.
            var loopback = (agent as WebAuthSessionPlugin)?.WindowsLoopback;
            if (loopback == null
                && HiveCore.TryResolve<IWindowsLoopbackAgent>(out var registered))
            {
                loopback = registered;
            }

            if (loopback != null)
            {
                var allocated = await loopback.AllocateLoopbackRedirectUriAsync(
                    new AllocateLoopbackRedirectUriRequest { PathPrefix = options.LoopbackPathPrefix },
                    cancellationToken);

                if (!(allocated is WindowsLoopbackServiceAllocateLoopbackRedirectUriResult.Success ok))
                {
                    return NonSuccess(SdkResultClassification.Of(allocated));
                }

                openUri = ok.Data.RedirectUri;

                if (options.UsesLoopbackRedirect)
                {
                    // No registered callback: hand the provider the bound origin. The listener is
                    // path-agnostic, so the advertised path can be dropped here while OpenAsync keeps
                    // the reserved URI whole.
                    authorizeUri = new Uri(openUri).GetLeftPart(UriPartial.Authority);
                }
                else
                {
                    authorizeUri = options.RedirectUri;
                    stateRoutingPrefix = new Uri(openUri).Port + ":";
                }
            }
            else if (apple)
            {
                if (string.IsNullOrWhiteSpace(relayCallbackUri))
                {
                    return ProviderCredentialOutcome.Failed(new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "Apple browser sign-in needs an app callback URI when no loopback agent is available."));
                }

                openUri = relayCallbackUri;
                authorizeUri = options.RedirectUri;
                // An http://127.0.0.1 callback uses the relay's port form, which returns to
                // http://127.0.0.1:<port>/. Apple's authorize endpoint rejects a literal
                // loopback URL in state.
                var callback = new Uri(relayCallbackUri);
                stateRoutingPrefix = callback.Scheme == Uri.UriSchemeHttp && callback.Host == "127.0.0.1"
                    ? callback.Port + ":"
                    : relayCallbackUri + "|";
            }
            else if (options.UsesLoopbackRedirect)
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Unavailable,
                    "No loopback allocator is registered, so a loopback redirect cannot be reserved. "
                    + "This platform needs a redirect URI the browser session can intercept."));
            }
            else
            {
                // No loopback here: the session intercepts the callback by the redirect URI's own
                // scheme, so the registered URI is used as-is on both sides.
                openUri = options.RedirectUri;
                authorizeUri = options.RedirectUri;
            }

            // Apple uses server-side credentials rather than provider PKCE.
            var pkce = apple ? default : Pkce.Create();

            // What the callback must echo. The relay strips the routing prefix before forwarding, so
            // the value to compare against is the state without it.
            var state = AuthorizeUrl.NewState();

            var url = AuthorizeUrl.Build(
                options.AuthorizeEndpoint, options.ClientId, authorizeUri, options.Scope,
                stateRoutingPrefix + state, apple ? null : pkce.Challenge, apple ? "form_post" : null);

            var opened = await agent.OpenAsync(
                new OpenRequest { Url = url, RedirectUri = openUri }, cancellationToken);

            var openClassification = SdkResultClassification.Of(opened);
            if (openClassification.Kind == SdkResultKind.UserCanceled)
            {
                return ProviderCredentialOutcome.Canceled();
            }

            if (!(opened is ExternalUserAgentServiceOpenResult.Success openedOk))
            {
                return NonSuccess(openClassification);
            }

            var parameters = openedOk.Data.Parameters ?? new Dictionary<string, string>();

            // The callback must echo the state this attempt started with, and that is checked before
            // anything else on it is read. RFC 6749 has the provider echo state on error responses
            // too, precisely so a forged error callback cannot slip past this check.
            parameters.TryGetValue("state", out var returnedState);
            if (!string.Equals(returnedState, state, StringComparison.Ordinal))
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.PermissionDenied,
                    "The callback did not carry the state this sign-in started with."));
            }

            // A provider reports a declined consent screen as an error on the callback rather than by
            // closing the browser, so access_denied is the user's cancellation.
            if (parameters.TryGetValue("error", out var error) && !string.IsNullOrEmpty(error))
            {
                return error == "access_denied"
                    ? ProviderCredentialOutcome.Canceled()
                    : ProviderCredentialOutcome.Failed(new HiveError(
                        HiveErrorCode.Internal,
                        $"The provider returned the authorization error '{error}'."));
            }

            if (!parameters.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Internal, "The callback carried no authorization code."));
            }

            // The provider requires the redirect URI presented here to match the one the
            // authorization request carried. A public client (no client_secret) the server cannot
            // exchange does the PKCE exchange itself; a confidential client hands the code to the
            // server. Both return the same credential, so the shared login join is unaffected.
            if (options.ClientSideExchange)
            {
                return await ProviderClientTokenExchange.ExchangeAsync(
                    provider, tokenProvider, options, code, authorizeUri, pkce.Verifier, cancellationToken);
            }

            return await ProviderCodeExchange.ExchangeAsync(
                auth, provider, tokenProvider, code, authorizeUri, apple ? null : pkce.Verifier, cancellationToken);
        }

        /// <summary>Reports a non-success result from the loopback or browser step.</summary>
        internal static ProviderCredentialOutcome NonSuccess(SdkResultClassification classification)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UntypedProblem:
                    return ProviderCredentialOutcome.Failed(classification.Problem);

                default:
                    // An outcome this build cannot name — a code newer than the SDK it was built
                    // against, or a declared variant added since. Keep the code and body as values;
                    // folding them into a message would lose what an app can act on, and calling it
                    // an internal error would claim a break that did not happen.
                    return ProviderCredentialOutcome.Unrecognized(
                        classification.UnknownCode, classification.RawJson);
            }
        }
    }
}

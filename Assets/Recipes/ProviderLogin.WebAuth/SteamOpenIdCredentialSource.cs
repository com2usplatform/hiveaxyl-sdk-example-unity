// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth.Addon.WebAuth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Signs in through Steam's OpenID page using WebAuth and returns a provider credential.
    /// </summary>
    /// <remarks>
    /// Requires a callback the WebAuth addon can capture. Callback matching does not authenticate
    /// the assertion; the returned credential still requires server verification.
    /// </remarks>
    public sealed class SteamOpenIdCredentialSource : IProviderCredentialSource
    {
        private readonly string m_returnTo;
        private readonly string m_redirectUri;
        private readonly string m_loginUrl;

        /// <param name="returnTo">
        /// The absolute http(s) URL Steam sends the player back to; the WebAuth addon must be able
        /// to capture it (on WebGL, the shipped callback page under the player's own origin).
        /// </param>
        /// <param name="redirectUri">
        /// Where the app itself expects the callback, when that differs from <paramref
        /// name="returnTo"/> — the app's own scheme, once a relay stands between Steam and the app.
        /// Leave null when the platform captures the http(s) redirect directly (a Windows loopback,
        /// the WebGL callback page), and <paramref name="returnTo"/> is used.
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="returnTo"/> is not an absolute http(s) URL — refused here, before any
        /// page opens.
        /// </exception>
        public SteamOpenIdCredentialSource(string returnTo, string redirectUri = null)
        {
            m_loginUrl = SteamOpenId.BuildLoginUrl(returnTo);
            m_returnTo = returnTo;
            m_redirectUri = string.IsNullOrWhiteSpace(redirectUri) ? returnTo : redirectUri;
        }

        /// <inheritdoc />
        public LoginProvider Provider => LoginProvider.Steam;

        /// <inheritdoc />
        public async Task<ProviderCredentialOutcome> AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            if (!HiveCore.TryResolve<IExternalUserAgent>(out var agent) || agent == null)
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Unavailable,
                    "IExternalUserAgent is not registered, so no browser sign-in can run in this build."));
            }

            // Nothing awaits before OpenAsync: on WebGL the popup is allowed only inside the click
            // that started the sign-in, and an await in between forfeits that.
            // RedirectUri is where the *app* waits, which is not always where Steam returns.
            // With a relay in between the two differ, and the platforms disagree on which one
            // matters: iOS drives ASWebAuthenticationSession's OS-level match from this scheme, so
            // handing it the relay's https would leave the session waiting for a callback that
            // never arrives; Android does not read the field at all and matches on its manifest
            // intent-filter instead, which is why the same value passes there and fails here.
            var opened = await agent.OpenAsync(
                new OpenRequest { Url = m_loginUrl, RedirectUri = m_redirectUri },
                cancellationToken);
            var classification = SdkResultClassification.Of(opened);
            if (classification.Kind == SdkResultKind.UserCanceled)
            {
                return ProviderCredentialOutcome.Canceled();
            }

            if (!(opened is ExternalUserAgentServiceOpenResult.Success ok))
            {
                return WebAuthCredentialFlow.NonSuccess(classification);
            }

            var parameters = ok.Data.Parameters ?? new Dictionary<string, string>();

            // Steam reports a declined sign-in on the callback rather than by closing the page.
            parameters.TryGetValue("openid.mode", out var mode);
            if (mode == "cancel")
            {
                return ProviderCredentialOutcome.Canceled();
            }

            if (mode != "id_res")
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Internal,
                    $"The callback carried openid.mode '{mode}' instead of a positive assertion."));
            }

            parameters.TryGetValue("openid.return_to", out var returnTo);
            if (!string.Equals(returnTo, m_returnTo, StringComparison.Ordinal))
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.PermissionDenied,
                    "The callback did not carry the return URL this sign-in started with."));
            }

            parameters.TryGetValue("openid.claimed_id", out var claimedId);
            if (!SteamOpenId.TryParseSteamId(claimedId, out var steamId))
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Internal,
                    "The callback's openid.claimed_id is not a Steam identity."));
            }

            return ProviderCredentialOutcome.Succeeded(ProviderCredential.Create(
                LoginProvider.Steam,
                SteamOpenId.ProviderTokenFrom(ok.Data.CallbackUrl),
                steamId));
        }
    }
}

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Exchanges an authorization code directly with a public OAuth provider using PKCE.
    /// </summary>
    internal static class ProviderClientTokenExchange
    {
        /// <summary>
        /// Exchanges the authorization code for a credential: an ID token for Google or an access token for X.
        /// </summary>
        internal static async Task<ProviderCredentialOutcome> ExchangeAsync(
            LoginProvider provider,
            Provider tokenProvider,
            WebAuthOptions options,
            string providerCode,
            string redirectUri,
            string codeVerifier,
            CancellationToken cancellationToken)
        {
            var exchange = new ProviderTokenExchange();

            ProviderTokenExchangeResult result;
            switch (tokenProvider)
            {
                case Provider.Google:
                    result = await exchange.ExchangeGoogleAsync(
                        options.TokenEndpoint, options.ClientId, providerCode, codeVerifier, redirectUri,
                        cancellationToken);
                    break;

                case Provider.X:
                    if (string.IsNullOrEmpty(options.UserInfoEndpoint))
                    {
                        return ProviderCredentialOutcome.Failed(new HiveError(
                            HiveErrorCode.InvalidArgument,
                            "X requires a userInfoEndpoint: its token endpoint returns no user id."));
                    }

                    result = await exchange.ExchangeXAsync(
                        options.TokenEndpoint, options.UserInfoEndpoint, options.ClientId, providerCode,
                        codeVerifier, redirectUri, cancellationToken);
                    break;

                default:
                    return ProviderCredentialOutcome.Failed(new HiveError(
                        HiveErrorCode.InvalidArgument,
                        $"Client-side token exchange is not supported for provider '{tokenProvider}'."));
            }

            if (result.IsSuccess)
            {
                return ProviderCredentialOutcome.Succeeded(ProviderCredential.Create(
                    provider, result.ProviderToken, result.ProviderUserId));
            }

            // A caller-cancelled exchange stays a Failure carrying HiveErrorCode.Cancelled — NOT
            // ProviderCredentialOutcome.Canceled(). That status means the user dismissed the
            // provider's UI, which the browser round trip already reports as access_denied
            // (WebAuthCredentialFlow); the client-side exchange has no such user-facing step.
            var error = result.Error
                ?? new HiveError(HiveErrorCode.Internal, "The client-side token exchange failed without a reason.");
            return ProviderCredentialOutcome.Failed(error);
        }
    }
}

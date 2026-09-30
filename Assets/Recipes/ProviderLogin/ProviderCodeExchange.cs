// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Exchanges a provider authorization code for a credential through the auth service.
    /// </summary>
    /// <remarks>
    /// The exchange is a server call, so its Outcomes are business results and are carried as values.
    /// Reducing them to a message would lose what an app branches on.
    /// </remarks>
    internal static class ProviderCodeExchange
    {
        /// <summary>
        /// Exchanges <paramref name="providerCode"/> for a provider token.
        /// </summary>
        /// <param name="auth">The auth Capability.</param>
        /// <param name="provider">The provider the resulting credential belongs to.</param>
        /// <param name="tokenProvider">The provider as the exchange endpoint names it.</param>
        /// <param name="providerCode">The code the provider issued.</param>
        /// <param name="redirectUri">
        /// The redirect URI the authorization used, where the provider requires the two to match, or
        /// null for a route with no redirect.
        /// </param>
        /// <param name="codeVerifier">The PKCE verifier, or null for a route without PKCE.</param>
        /// <param name="cancellationToken">The caller's token.</param>
        internal static async Task<ProviderCredentialOutcome> ExchangeAsync(
            IAuthService auth,
            LoginProvider provider,
            Provider tokenProvider,
            string providerCode,
            string redirectUri,
            string codeVerifier,
            CancellationToken cancellationToken)
        {
            var exchanged = await auth.ExchangeProviderTokenAsync(
                new ProviderTokenRequest
                {
                    ProviderId = tokenProvider,
                    ProviderCode = providerCode,
                    RedirectUri = redirectUri,
                    CodeVerifier = codeVerifier,
                },
                new ApiCallContext { Token = cancellationToken });

            var classification = SdkResultClassification.Of(exchanged);
            if (classification.Kind == SdkResultKind.Success
                && exchanged is AuthExchangeProviderTokenResult.Success ok)
            {
                return ProviderCredentialOutcome.Succeeded(ProviderCredential.Create(
                    provider, ok.Data.ProviderToken, ok.Data.ProviderUserId));
            }

            switch (classification.Kind)
            {
                case SdkResultKind.UserCanceled:
                    return ProviderCredentialOutcome.Canceled();

                case SdkResultKind.UntypedProblem:
                    return ProviderCredentialOutcome.Failed(classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    return ProviderCredentialOutcome.Unrecognized(
                        classification.UnknownCode, classification.RawJson);

                default:
                    var business = Map(exchanged);
                    return business == ProviderLoginBusinessOutcome.Unrecognized
                        ? ProviderCredentialOutcome.Unrecognized(string.Empty, classification.RawJson)
                        : ProviderCredentialOutcome.Business(business, classification.RawJson);
            }
        }

        private static ProviderLoginBusinessOutcome Map(AuthExchangeProviderTokenResult result)
        {
            switch (result)
            {
                case AuthExchangeProviderTokenResult.ProviderNotSupported _:
                    return ProviderLoginBusinessOutcome.ProviderNotSupported;
                case AuthExchangeProviderTokenResult.ProviderTokenExchangeNotSupported _:
                    return ProviderLoginBusinessOutcome.ProviderTokenExchangeNotSupported;
                case AuthExchangeProviderTokenResult.ProviderTokenError _:
                    return ProviderLoginBusinessOutcome.ProviderTokenError;
                case AuthExchangeProviderTokenResult.ProviderConfigNotFound _:
                    return ProviderLoginBusinessOutcome.ProviderConfigNotFound;
                case AuthExchangeProviderTokenResult.ProviderClientInfoNotExists _:
                    return ProviderLoginBusinessOutcome.ProviderClientInfoNotExists;
                case AuthExchangeProviderTokenResult.ProviderRequestFailed _:
                    return ProviderLoginBusinessOutcome.ProviderRequestFailed;
                case AuthExchangeProviderTokenResult.TerminateService _:
                    return ProviderLoginBusinessOutcome.ServiceTerminated;
                case AuthExchangeProviderTokenResult.AppNotFound _:
                    return ProviderLoginBusinessOutcome.AppNotFound;
                default:
                    return ProviderLoginBusinessOutcome.Unrecognized;
            }
        }
    }
}

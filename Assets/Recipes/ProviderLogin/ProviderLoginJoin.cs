// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Logs in with a provider credential and installs the resulting session.
    /// </summary>
    internal static class ProviderLoginJoin
    {
        /// <summary>
        /// Logs the provider credential in and installs the resulting session.
        /// </summary>
        /// <param name="auth">The auth Capability. Must not be null.</param>
        /// <param name="tokens">The token Capability. Must not be null.</param>
        /// <param name="session">Core's session state. Must not be null.</param>
        /// <param name="providerId">The provider the credential belongs to.</param>
        /// <param name="providerUserId">
        /// The provider's user id, or empty when the server derives it from the credential itself
        /// (Steam reads it out of the ticket).
        /// </param>
        /// <param name="providerToken">The provider token or ticket to verify.</param>
        /// <param name="clientId">The PKCE client id this app logs in as.</param>
        /// <param name="deviceKey">The app's device identifier.</param>
        /// <param name="cancellationToken">The caller's token, forwarded to both calls.</param>
        /// <returns>
        /// The resolution, stamped with the half it stopped at so the caller can name the step in its
        /// own vocabulary.
        /// </returns>
        internal static async Task<ProviderLoginResolution> CompleteAsync(
            IAuthService auth,
            ITokenService tokens,
            ISessionManager session,
            Provider providerId,
            string providerUserId,
            string providerToken,
            string clientId,
            string deviceKey,
            CancellationToken cancellationToken)
        {
            if (auth == null)
            {
                throw new ArgumentNullException(nameof(auth));
            }

            // This pair belongs to the Axyl authorization code, not to the provider's own OAuth
            // exchange: the challenge goes up with the login, the verifier with the token call below.
            var pkce = Pkce.Create();

            var login = await auth.LoginProviderAsync(
                new ProviderLoginRequest
                {
                    ProviderId = providerId,
                    ProviderUserId = providerUserId ?? string.Empty,
                    ProviderToken = providerToken,
                    ClientId = clientId,
                    DeviceKey = deviceKey,
                    CodeChallenge = pkce.Challenge,
                    CodeChallengeMethod = CodeChallengeMethod.S256,
                },
                new ApiCallContext { Token = cancellationToken });

            var classification = SdkResultClassification.Of(login);
            if (classification.Kind != SdkResultKind.Success
                || !(login is AuthLoginProviderResult.Success success))
            {
                return ProviderLoginResolution
                    .FromClassification(classification, MapLoginProvider(login))
                    .AtJoinStep(ProviderLoginJoinStep.LoginProvider);
            }

            if (string.IsNullOrEmpty(success.Data.AuthorizationCode))
            {
                return ProviderLoginResolution
                    .Failed(new HiveError(
                        HiveErrorCode.Internal,
                        "The provider login succeeded without an authorization code, so no token could be issued."))
                    .AtJoinStep(ProviderLoginJoinStep.LoginProvider);
            }

            var setup = await SessionSetup.EstablishAsync(
                tokens, session, clientId, success.Data.AuthorizationCode, pkce.Verifier,
                success.Data.PlayerId, cancellationToken);

            switch (setup.Status)
            {
                case SessionSetupStatus.Established:
                    return ProviderLoginResolution.Established(
                        success.Data.PlayerId, success.Data.IsBlock);

                case SessionSetupStatus.TokenCallFailed:
                    return ProviderLoginResolution
                        .FromClassification(setup.Classification, MapIssueToken(setup.TokenResult))
                        .AtJoinStep(ProviderLoginJoinStep.SessionSetup);

                case SessionSetupStatus.Cancelled:
                    return ProviderLoginResolution
                        .Failed(new HiveError(
                            HiveErrorCode.Cancelled,
                            "The caller canceled before the session was installed."))
                        .AtJoinStep(ProviderLoginJoinStep.SessionSetup);

                case SessionSetupStatus.UnusableTokenResponse:
                    return ProviderLoginResolution
                        .Failed(setup.Problem)
                        .AtJoinStep(ProviderLoginJoinStep.SessionSetup);

                // Named rather than left to the arm below, because Problem is filled in for that
                // status alone. A status added later and missed here would otherwise arrive with a
                // null error and take the caller down on the first read of it.
                default:
                    return ProviderLoginResolution
                        .Failed(new HiveError(
                            HiveErrorCode.Internal,
                            $"Unhandled session-setup status: {setup.Status}."))
                        .AtJoinStep(ProviderLoginJoinStep.SessionSetup);
            }
        }

        private static ProviderLoginBusinessOutcome MapLoginProvider(AuthLoginProviderResult result)
        {
            switch (result)
            {
                case AuthLoginProviderResult.ProviderTokenError _:
                    return ProviderLoginBusinessOutcome.ProviderTokenError;
                case AuthLoginProviderResult.ProviderConfigNotFound _:
                    return ProviderLoginBusinessOutcome.ProviderConfigNotFound;
                case AuthLoginProviderResult.ProviderClientInfoNotExists _:
                    return ProviderLoginBusinessOutcome.ProviderClientInfoNotExists;
                case AuthLoginProviderResult.ProviderRequestFailed _:
                    return ProviderLoginBusinessOutcome.ProviderRequestFailed;
                case AuthLoginProviderResult.TerminateService _:
                    return ProviderLoginBusinessOutcome.ServiceTerminated;
                case AuthLoginProviderResult.InvalidClientId _:
                    return ProviderLoginBusinessOutcome.InvalidClient;
                case AuthLoginProviderResult.IpBlocked _:
                    return ProviderLoginBusinessOutcome.IpBlocked;
                case AuthLoginProviderResult.AppNotFound _:
                    return ProviderLoginBusinessOutcome.AppNotFound;
                default:
                    return ProviderLoginBusinessOutcome.Unrecognized;
            }
        }

        private static ProviderLoginBusinessOutcome MapIssueToken(TokenIssueTokenResult result)
        {
            switch (result)
            {
                case TokenIssueTokenResult.InvalidClient _:
                    return ProviderLoginBusinessOutcome.InvalidClient;
                case TokenIssueTokenResult.UnsupportedGrantType _:
                    return ProviderLoginBusinessOutcome.UnsupportedGrantType;
                case TokenIssueTokenResult.InvalidGrantExpired _:
                    return ProviderLoginBusinessOutcome.ExpiredAuthorizationCode;
                case TokenIssueTokenResult.InvalidGrantCodeChallenge _:
                    return ProviderLoginBusinessOutcome.CodeChallengeMismatch;
                case TokenIssueTokenResult.InvalidGrantRefreshToken _:
                    return ProviderLoginBusinessOutcome.InvalidRefreshToken;
                case TokenIssueTokenResult.InvalidGrant _:
                    // Checked after the more specific invalid_grant variants above.
                    return ProviderLoginBusinessOutcome.InvalidAuthorizationCode;
                case TokenIssueTokenResult.AppNotFound _:
                    return ProviderLoginBusinessOutcome.AppNotFound;
                case TokenIssueTokenResult.TemporarilyUnavailable _:
                    return ProviderLoginBusinessOutcome.TemporarilyUnavailable;
                default:
                    return ProviderLoginBusinessOutcome.Unrecognized;
            }
        }
    }
}

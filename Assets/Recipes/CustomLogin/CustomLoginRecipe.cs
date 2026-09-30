// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Signs a player in on the strength of a grant key the game's own server issued, and leaves a
    /// live session behind.
    /// </summary>
    /// <remarks>
    /// Requires initialized Core with <c>AddAuth</c> and <c>AddToken</c>.
    /// Obtain a one-time grant key from the game's server after authenticating the player.
    /// This Recipe does not retry. Obtain a fresh key after rejection or uncertain redemption.
    /// A failed attempt leaves the existing session untouched. Cancellation does not undo key redemption.
    /// </remarks>
    public sealed class CustomLoginRecipe
    {
        private readonly string m_clientId;
        private readonly string m_deviceKey;

        /// <summary>
        /// Creates the Recipe with the identity the app was provisioned with.
        /// </summary>
        /// <param name="clientId">
        /// The PKCE client id this app logs in as. Must not be null or whitespace.
        /// </param>
        /// <param name="deviceKey">
        /// The app's device identifier. Keep it stable across launches. Must not be blank.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when either argument is null, empty, or whitespace.
        /// </exception>
        public CustomLoginRecipe(string clientId, string deviceKey)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new ArgumentException(
                    "clientId must be a non-empty, non-whitespace string.", nameof(clientId));
            }

            if (string.IsNullOrWhiteSpace(deviceKey))
            {
                throw new ArgumentException(
                    "deviceKey must be a non-empty, non-whitespace string.", nameof(deviceKey));
            }

            m_clientId = clientId;
            m_deviceKey = deviceKey;
        }

        /// <summary>
        /// Redeems <paramref name="grantKey"/> for a session.
        /// </summary>
        /// <param name="grantKey">
        /// The unused, one-time key obtained from the game's server. Must not be null or whitespace.
        /// </param>
        /// <param name="cancellationToken">
        /// The caller's cancellation token, forwarded to every SDK call.
        /// </param>
        /// <returns>
        /// The attempt's outcome. A key the server will not take comes back as
        /// <see cref="LoginWithCustomBusinessOutcome.InvalidGrantKey"/>, which
        /// <see cref="LoginWithCustomOutcome.IsGrantKeyRejected"/> marks — fetch another and call again.
        /// </returns>
        public async Task<LoginWithCustomOutcome> LoginWithCustomAsync(
            string grantKey, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return LoginWithCustomOutcome.Failed(
                    LoginWithCustomStep.CustomLogin, new HiveError(
                        HiveErrorCode.Cancelled, "The caller canceled the login before it started."));
            }

            if (string.IsNullOrWhiteSpace(grantKey))
            {
                return LoginWithCustomOutcome.Failed(
                    LoginWithCustomStep.Validate, new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "A grant key is required. Obtain a fresh key from the game's server."));
            }

            if (!HiveCore.TryResolve<IAuthService>(out var auth) || auth == null)
            {
                return LoginWithCustomOutcome.Failed(LoginWithCustomStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "IAuthService is not registered. Initialize the SDK with AddAuth first."));
            }

            if (!HiveCore.TryResolve<ITokenService>(out var tokens) || tokens == null)
            {
                return LoginWithCustomOutcome.Failed(LoginWithCustomStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "ITokenService is not registered. Initialize the SDK with AddToken first."));
            }

            if (!HiveCore.TryResolve<ISessionManager>(out var session) || session == null)
            {
                return LoginWithCustomOutcome.Failed(LoginWithCustomStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "ISessionManager is not available. The SDK Core is not initialized."));
            }

            // One verifier/challenge pair spans this attempt: the challenge goes with the login call,
            // the verifier with the token exchange that follows it.
            var pkce = Pkce.Create();

            var loggedIn = await auth.LoginCustomProviderAsync(
                new CustomLoginRequest
                {
                    GrantKey = grantKey,
                    ClientId = m_clientId,
                    CodeChallenge = pkce.Challenge,
                    CodeChallengeMethod = CodeChallengeMethod.S256,
                    DeviceKey = m_deviceKey,
                },
                new ApiCallContext { Token = cancellationToken });

            var classification = SdkResultClassification.Of(loggedIn);
            if (classification.Kind != SdkResultKind.Success
                || !(loggedIn is AuthLoginCustomProviderResult.Success loggedInOk))
            {
                return Translate(
                    LoginWithCustomStep.CustomLogin, classification, MapCustomLogin(loggedIn));
            }

            var playerId = loggedInOk.Data.PlayerId;
            var authorizationCode = loggedInOk.Data.AuthorizationCode;

            if (string.IsNullOrEmpty(authorizationCode))
            {
                return LoginWithCustomOutcome.Failed(
                    LoginWithCustomStep.CustomLogin, new HiveError(
                        HiveErrorCode.Internal,
                        "The login succeeded without an authorization code, so no token could be issued."));
            }

            var setup = await SessionSetup.EstablishAsync(
                tokens, session, m_clientId, authorizationCode, pkce.Verifier, playerId,
                cancellationToken);

            switch (setup.Status)
            {
                case SessionSetupStatus.Established:
                    return LoginWithCustomOutcome.Succeeded(
                        playerId, loggedInOk.Data.IsBlock);

                case SessionSetupStatus.TokenCallFailed:
                    return Translate(
                        LoginWithCustomStep.SessionSetup, setup.Classification,
                        MapIssueToken(setup.TokenResult));

                case SessionSetupStatus.Cancelled:
                    return LoginWithCustomOutcome.Failed(
                        LoginWithCustomStep.SessionSetup,
                        new HiveError(
                            HiveErrorCode.Cancelled,
                            "The caller canceled before the session was installed."));

                case SessionSetupStatus.UnusableTokenResponse:
                    return LoginWithCustomOutcome.Failed(
                        LoginWithCustomStep.SessionSetup, setup.Problem);

                // Named rather than left to the arm below, because Problem is filled in for that
                // status alone. A status added later and missed here would otherwise arrive with a
                // null error and take the caller down on the first read of it.
                default:
                    return LoginWithCustomOutcome.Failed(
                        LoginWithCustomStep.SessionSetup,
                        new HiveError(
                            HiveErrorCode.Internal,
                            $"Unhandled session-setup status: {setup.Status}."));
            }
        }

        /// <summary>
        /// Turns a classified SDK result into this Recipe's outcome. Only the
        /// <see cref="SdkResultKind.TypedOutcome"/> branch consults
        /// <paramref name="businessOutcome"/>; the rest carry everything they need already.
        /// </summary>
        private static LoginWithCustomOutcome Translate(
            LoginWithCustomStep step,
            SdkResultClassification classification,
            LoginWithCustomBusinessOutcome businessOutcome)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UntypedProblem:
                    return LoginWithCustomOutcome.Failed(step, classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    return LoginWithCustomOutcome.Unrecognized(
                        step, classification.UnknownCode, classification.RawJson);

                default:
                    // A typed Outcome this Recipe has no value for stays unrecognized rather than
                    // being folded into a neighbouring meaning.
                    return businessOutcome == LoginWithCustomBusinessOutcome.Unrecognized
                        ? LoginWithCustomOutcome.Unrecognized(
                            step, string.Empty, classification.RawJson)
                        : LoginWithCustomOutcome.Business(
                            step, businessOutcome, classification.RawJson);
            }
        }

        private static LoginWithCustomBusinessOutcome MapCustomLogin(
            AuthLoginCustomProviderResult result)
        {
            switch (result)
            {
                case AuthLoginCustomProviderResult.InvalidGrantKey _:
                    return LoginWithCustomBusinessOutcome.InvalidGrantKey;
                case AuthLoginCustomProviderResult.TerminateService _:
                    return LoginWithCustomBusinessOutcome.ServiceTerminated;
                case AuthLoginCustomProviderResult.InvalidClientId _:
                    return LoginWithCustomBusinessOutcome.InvalidClient;
                case AuthLoginCustomProviderResult.IpBlocked _:
                    return LoginWithCustomBusinessOutcome.IpBlocked;
                case AuthLoginCustomProviderResult.AppNotFound _:
                    return LoginWithCustomBusinessOutcome.AppNotFound;
                case AuthLoginCustomProviderResult.ProviderConfigNotFound _:
                    return LoginWithCustomBusinessOutcome.ProviderConfigNotFound;
                case AuthLoginCustomProviderResult.ProviderNotSupported _:
                    return LoginWithCustomBusinessOutcome.ProviderNotSupported;
                default:
                    return LoginWithCustomBusinessOutcome.Unrecognized;
            }
        }

        private static LoginWithCustomBusinessOutcome MapIssueToken(TokenIssueTokenResult result)
        {
            switch (result)
            {
                case TokenIssueTokenResult.InvalidClient _:
                    return LoginWithCustomBusinessOutcome.InvalidClient;
                case TokenIssueTokenResult.UnsupportedGrantType _:
                    return LoginWithCustomBusinessOutcome.UnsupportedGrantType;
                case TokenIssueTokenResult.InvalidGrantExpired _:
                    return LoginWithCustomBusinessOutcome.ExpiredAuthorizationCode;
                case TokenIssueTokenResult.InvalidGrantCodeChallenge _:
                    return LoginWithCustomBusinessOutcome.CodeChallengeMismatch;
                case TokenIssueTokenResult.InvalidGrantRefreshToken _:
                    return LoginWithCustomBusinessOutcome.InvalidRefreshToken;
                case TokenIssueTokenResult.InvalidGrant _:
                    // Checked after the more specific invalid_grant variants above.
                    return LoginWithCustomBusinessOutcome.InvalidAuthorizationCode;
                case TokenIssueTokenResult.AppNotFound _:
                    return LoginWithCustomBusinessOutcome.AppNotFound;
                case TokenIssueTokenResult.TemporarilyUnavailable _:
                    return LoginWithCustomBusinessOutcome.TemporarilyUnavailable;
                default:
                    return LoginWithCustomBusinessOutcome.Unrecognized;
            }
        }
    }
}

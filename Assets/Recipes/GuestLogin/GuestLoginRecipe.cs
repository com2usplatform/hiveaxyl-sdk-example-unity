// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Logs a player in as a Guest: creates the account on a first run, or restores the one the app
    /// kept a <see cref="GuestCredential"/> for, and leaves a live session behind either way.
    /// </summary>
    /// <remarks>
    /// Requires initialized Core with <c>AddAuth</c> and <c>AddToken</c>.
    /// Persist any returned <see cref="LoginAsGuestOutcome.Credential"/>, including on failure.
    /// A failed restore does not create a new account. The app owns credential storage and retry decisions.
    /// A failed attempt leaves the existing session untouched.
    /// </remarks>
    public sealed class GuestLoginRecipe
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
        public GuestLoginRecipe(string clientId, string deviceKey)
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
        /// Logs in as a Guest and installs the session.
        /// </summary>
        /// <param name="credential">
        /// The credential a previous run produced, to restore that account. Pass <c>null</c> to
        /// create a new Guest account.
        /// </param>
        /// <param name="cancellationToken">
        /// The caller's cancellation token, forwarded to every SDK call. Cancelling it is reported as
        /// <c>Failure</c> with <see cref="HiveErrorCode.Cancelled"/>.
        /// </param>
        /// <returns>
        /// The attempt's outcome. On success, <see cref="LoginAsGuestOutcome.Credential"/> is what the
        /// app must persist to restore this account later.
        /// </returns>
        public async Task<LoginAsGuestOutcome> LoginAsGuestAsync(
            GuestCredential credential = null, CancellationToken cancellationToken = default)
        {
            var loginStep = credential == null
                ? LoginAsGuestStep.CreateGuest
                : LoginAsGuestStep.RestoreGuest;

            if (cancellationToken.IsCancellationRequested)
            {
                return LoginAsGuestOutcome.Failed(loginStep, new HiveError(
                    HiveErrorCode.Cancelled, "The caller canceled the Guest login before it started."));
            }

            if (!HiveCore.TryResolve<IAuthService>(out var auth) || auth == null)
            {
                return LoginAsGuestOutcome.Failed(LoginAsGuestStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "IAuthService is not registered. Initialize the SDK with AddAuth first."));
            }

            if (!HiveCore.TryResolve<ITokenService>(out var tokens) || tokens == null)
            {
                return LoginAsGuestOutcome.Failed(LoginAsGuestStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "ITokenService is not registered. Initialize the SDK with AddToken first."));
            }

            if (!HiveCore.TryResolve<ISessionManager>(out var session) || session == null)
            {
                return LoginAsGuestOutcome.Failed(LoginAsGuestStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "ISessionManager is not available. The SDK Core is not initialized."));
            }

            // One verifier/challenge pair spans this attempt: the challenge goes with the login call,
            // the verifier with the token exchange that follows it.
            var pkce = Pkce.Create();

            long playerId;
            string authorizationCode;
            GuestCredential established;
            var isBlocked = false;

            if (credential == null)
            {
                var created = await auth.CreateGuestAsync(
                    new GuestCreateRequest
                    {
                        ClientId = m_clientId,
                        CodeChallenge = pkce.Challenge,
                        CodeChallengeMethod = CodeChallengeMethod.S256,
                        DeviceKey = m_deviceKey,
                    },
                    new ApiCallContext { Token = cancellationToken });

                var classification = SdkResultClassification.Of(created);
                if (classification.Kind != SdkResultKind.Success
                    || !(created is AuthCreateGuestResult.Success createdOk))
                {
                    return Translate(
                        LoginAsGuestStep.CreateGuest, classification, MapCreateGuest(created));
                }

                playerId = createdOk.Data.PlayerId;
                authorizationCode = createdOk.Data.AuthorizationCode;

                if (string.IsNullOrEmpty(createdOk.Data.GuestToken))
                {
                    return LoginAsGuestOutcome.Failed(
                        LoginAsGuestStep.CreateGuest, new HiveError(
                            HiveErrorCode.Internal,
                            "Guest creation returned no guest token, so the account could never be restored."));
                }

                established = GuestCredential.Create(playerId, createdOk.Data.GuestToken);
            }
            else
            {
                var restored = await auth.LoginGuestAsync(
                    new GuestLoginRequest
                    {
                        GuestPlayerId = credential.PlayerId,
                        GuestToken = credential.GuestToken,
                        ClientId = m_clientId,
                        CodeChallenge = pkce.Challenge,
                        CodeChallengeMethod = CodeChallengeMethod.S256,
                        DeviceKey = m_deviceKey,
                    },
                    new ApiCallContext { Token = cancellationToken });

                var classification = SdkResultClassification.Of(restored);
                if (classification.Kind != SdkResultKind.Success
                    || !(restored is AuthLoginGuestResult.Success restoredOk))
                {
                    return Translate(
                        LoginAsGuestStep.RestoreGuest, classification, MapLoginGuest(restored));
                }

                playerId = restoredOk.Data.PlayerId;
                authorizationCode = restoredOk.Data.AuthorizationCode;
                isBlocked = restoredOk.Data.IsBlock;

                // The token the app already holds stays the way back into this account.
                established = credential;
            }

            // The account exists; preserve its credential on every subsequent outcome.
            if (string.IsNullOrEmpty(authorizationCode))
            {
                return LoginAsGuestOutcome.Failed(
                    loginStep,
                    new HiveError(
                        HiveErrorCode.Internal,
                        "The login succeeded without an authorization code, so no token could be issued."),
                    established);
            }

            var setup = await SessionSetup.EstablishAsync(
                tokens, session, m_clientId, authorizationCode, pkce.Verifier, playerId,
                cancellationToken);

            switch (setup.Status)
            {
                case SessionSetupStatus.Established:
                    return LoginAsGuestOutcome.Succeeded(
                        established, credential == null, isBlocked);

                case SessionSetupStatus.TokenCallFailed:
                    return Translate(
                        LoginAsGuestStep.SessionSetup, setup.Classification,
                        MapIssueToken(setup.TokenResult), established);

                // Cancellation does not undo account creation.
                case SessionSetupStatus.Cancelled:
                    return LoginAsGuestOutcome.Failed(
                        LoginAsGuestStep.SessionSetup,
                        new HiveError(
                            HiveErrorCode.Cancelled,
                            "The caller canceled before the session was installed."),
                        established);

                case SessionSetupStatus.UnusableTokenResponse:
                    return LoginAsGuestOutcome.Failed(
                        LoginAsGuestStep.SessionSetup, setup.Problem, established);

                // Named rather than left to the arm below, because Problem is filled in for that
                // status alone. A status added later and missed here would otherwise arrive with a
                // null error and take the caller down on the first read of it.
                default:
                    return LoginAsGuestOutcome.Failed(
                        LoginAsGuestStep.SessionSetup,
                        new HiveError(
                            HiveErrorCode.Internal,
                            $"Unhandled session-setup status: {setup.Status}."),
                        established);
            }
        }

        /// <summary>
        /// Turns a classified SDK result into this Recipe's outcome. Only the
        /// <see cref="SdkResultKind.TypedOutcome"/> branch consults
        /// <paramref name="businessOutcome"/>; the rest carry everything they need already.
        /// </summary>
        private static LoginAsGuestOutcome Translate(
            LoginAsGuestStep step,
            SdkResultClassification classification,
            LoginAsGuestBusinessOutcome businessOutcome,
            GuestCredential credential = null)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UntypedProblem:
                    return LoginAsGuestOutcome.Failed(step, classification.Problem, credential);

                case SdkResultKind.UnknownOutcome:
                    return LoginAsGuestOutcome.Unrecognized(
                        step, classification.UnknownCode, classification.RawJson, credential);

                default:
                    // A typed Outcome this Recipe has no value for stays unrecognized rather than
                    // being folded into a neighbouring meaning.
                    return businessOutcome == LoginAsGuestBusinessOutcome.Unrecognized
                        ? LoginAsGuestOutcome.Unrecognized(
                            step, string.Empty, classification.RawJson, credential)
                        : LoginAsGuestOutcome.Business(
                            step, businessOutcome, classification.RawJson, credential);
            }
        }

        private static LoginAsGuestBusinessOutcome MapCreateGuest(AuthCreateGuestResult result)
        {
            switch (result)
            {
                case AuthCreateGuestResult.InvalidGrantKey _:
                    return LoginAsGuestBusinessOutcome.InvalidGrantKey;
                case AuthCreateGuestResult.GrantRequiredMissing _:
                    return LoginAsGuestBusinessOutcome.GrantKeyRequired;
                case AuthCreateGuestResult.TerminateService _:
                    return LoginAsGuestBusinessOutcome.ServiceTerminated;
                case AuthCreateGuestResult.InvalidClientId _:
                    return LoginAsGuestBusinessOutcome.InvalidClient;
                case AuthCreateGuestResult.IpBlocked _:
                    return LoginAsGuestBusinessOutcome.IpBlocked;
                case AuthCreateGuestResult.ProviderConfigNotFound _:
                    return LoginAsGuestBusinessOutcome.ProviderConfigNotFound;
                case AuthCreateGuestResult.AppNotFound _:
                    return LoginAsGuestBusinessOutcome.AppNotFound;
                default:
                    return LoginAsGuestBusinessOutcome.Unrecognized;
            }
        }

        private static LoginAsGuestBusinessOutcome MapLoginGuest(AuthLoginGuestResult result)
        {
            switch (result)
            {
                case AuthLoginGuestResult.InvalidGuestToken _:
                    return LoginAsGuestBusinessOutcome.InvalidGuestToken;
                case AuthLoginGuestResult.TerminateService _:
                    return LoginAsGuestBusinessOutcome.ServiceTerminated;
                case AuthLoginGuestResult.InvalidClientId _:
                    return LoginAsGuestBusinessOutcome.InvalidClient;
                case AuthLoginGuestResult.IpBlocked _:
                    return LoginAsGuestBusinessOutcome.IpBlocked;
                case AuthLoginGuestResult.AppNotFound _:
                    return LoginAsGuestBusinessOutcome.AppNotFound;
                default:
                    return LoginAsGuestBusinessOutcome.Unrecognized;
            }
        }

        private static LoginAsGuestBusinessOutcome MapIssueToken(TokenIssueTokenResult result)
        {
            switch (result)
            {
                case TokenIssueTokenResult.InvalidClient _:
                    return LoginAsGuestBusinessOutcome.InvalidClient;
                case TokenIssueTokenResult.UnsupportedGrantType _:
                    return LoginAsGuestBusinessOutcome.UnsupportedGrantType;
                case TokenIssueTokenResult.InvalidGrantExpired _:
                    return LoginAsGuestBusinessOutcome.ExpiredAuthorizationCode;
                case TokenIssueTokenResult.InvalidGrantCodeChallenge _:
                    return LoginAsGuestBusinessOutcome.CodeChallengeMismatch;
                case TokenIssueTokenResult.InvalidGrantRefreshToken _:
                    return LoginAsGuestBusinessOutcome.InvalidRefreshToken;
                case TokenIssueTokenResult.InvalidGrant _:
                    // Checked after the more specific invalid_grant variants above.
                    return LoginAsGuestBusinessOutcome.InvalidAuthorizationCode;
                case TokenIssueTokenResult.AppNotFound _:
                    return LoginAsGuestBusinessOutcome.AppNotFound;
                case TokenIssueTokenResult.TemporarilyUnavailable _:
                    return LoginAsGuestBusinessOutcome.TemporarilyUnavailable;
                default:
                    return LoginAsGuestBusinessOutcome.Unrecognized;
            }
        }
    }
}

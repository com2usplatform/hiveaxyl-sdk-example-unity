// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Restores the session a previous run ended with, so a returning player does not sign in again.
    /// </summary>
    /// <remarks>
    /// Requires initialized Core with <c>AddAuth</c> and <c>AddToken</c>.
    /// Run at startup before establishing another session. Active-session refresh is handled by the SDK.
    /// The app owns secure credential storage. Check <see cref="AutoLoginOutcome.StoredCredentialIsStale"/>
    /// before discarding stored credentials; failure alone does not make them invalid.
    /// A refresh token may already be spent after failure or cancellation. Do not automatically retry it.
    /// </remarks>
    public sealed class AutoLoginRecipe
    {
        // An access token that expires while the request is in flight is no use, so treat the last
        // half-minute of its life as already gone rather than spending a round trip to find out.
        private const long k_ExpirySkewSeconds = 30;

        private readonly string m_clientId;

        /// <summary>
        /// Creates the Recipe with the identity the app was provisioned with.
        /// </summary>
        /// <param name="clientId">
        /// The PKCE client id this app logs in as. Must not be null or whitespace.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="clientId"/> is null, empty, or whitespace.
        /// </exception>
        public AutoLoginRecipe(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new ArgumentException(
                    "clientId must be a non-empty, non-whitespace string.", nameof(clientId));
            }

            m_clientId = clientId;
        }

        /// <summary>
        /// Restores the session from <paramref name="stored"/>.
        /// </summary>
        /// <param name="stored">
        /// What the app kept from the last run. Pass <c>null</c> when nothing was stored — that is
        /// reported as <see cref="AutoLoginStatus.NoSession"/>, which is a first launch, not a
        /// failure.
        /// </param>
        /// <param name="cancellationToken">
        /// The caller's cancellation token, forwarded to every SDK call.
        /// When cancellation is handled, the result is <c>Failure</c> with
        /// <see cref="HiveErrorCode.Cancelled"/>.
        /// Any SDK session established by this attempt is cleared. The app's storage is not modified.
        /// </param>
        /// <returns>The attempt's outcome.</returns>
        public async Task<AutoLoginOutcome> LoginAsync(
            StoredSession stored, CancellationToken cancellationToken = default)
        {
            // Before anything else: a caller who already canceled is told why they stopped, not
            // what else happened to be missing.
            if (cancellationToken.IsCancellationRequested)
            {
                return AutoLoginOutcome.Failed(AutoLoginStep.Resolve, new HiveError(
                    HiveErrorCode.Cancelled, "The caller canceled the auto login before it started."));
            }

            if (stored == null)
            {
                return AutoLoginOutcome.NoSession();
            }

            if (!HiveCore.TryResolve<IAuthService>(out var auth) || auth == null)
            {
                return AutoLoginOutcome.Failed(AutoLoginStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "IAuthService is not registered. Initialize the SDK with AddAuth first."));
            }

            if (!HiveCore.TryResolve<ITokenService>(out var tokens) || tokens == null)
            {
                return AutoLoginOutcome.Failed(AutoLoginStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "ITokenService is not registered. Initialize the SDK with AddToken first."));
            }

            if (!HiveCore.TryResolve<ISessionManager>(out var session) || session == null)
            {
                return AutoLoginOutcome.Failed(AutoLoginStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "ISessionManager is not available. The SDK Core is not initialized."));
            }

            if (AccessTokenIsUsable(stored.AccessToken))
            {
                return await RestoreWithAccessTokenAsync(
                    auth, tokens, session, stored, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(stored.RefreshToken))
            {
                return await RefreshAsync(tokens, session, stored, cancellationToken);
            }

            // An access token past its expiry and no refresh token behind it: the app is holding a
            // credential, but there is nothing left in it. Same destination as a first launch, with
            // the extra note that what is on disk can go.
            return AutoLoginOutcome.NoSession(storedCredentialIsStale: true);
        }

        /// <summary>
        /// Validates the stored access token against the server, then installs the pair the server
        /// issues back.
        /// </summary>
        /// <remarks>
        /// The stored access token is used only as this request's credential via
        /// <see cref="ApiCallContext.WithAccessToken"/>.
        /// The SDK session is established only after login and token issuance succeed.
        /// A 401 response to this request does not trigger the SDK's automatic token refresh.
        /// </remarks>
        private async Task<AutoLoginOutcome> RestoreWithAccessTokenAsync(
            IAuthService auth,
            ITokenService tokens,
            ISessionManager session,
            StoredSession stored,
            CancellationToken cancellationToken)
        {
            var pkce = Pkce.Create();

            var context = ApiCallContext.WithAccessToken(stored.AccessToken);
            context.Token = cancellationToken;
            var result = await auth.LoginWithAccessTokenAsync(
                new TokenLoginRequest
                {
                    ClientId = m_clientId,
                    CodeChallenge = pkce.Challenge,
                    CodeChallengeMethod = CodeChallengeMethod.S256,
                },
                context);

            // A cancel does not reach into a request already in flight, so a result can land after
            // the caller stopped waiting. It must not finish the restore as a success.
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(AutoLoginStep.RestoreWithAccessToken);
            }

            var classification = SdkResultClassification.Of(result);
            if (classification.Kind != SdkResultKind.Success
                || !(result is AuthLoginWithAccessTokenResult.Success ok))
            {
                return Translate(
                    AutoLoginStep.RestoreWithAccessToken,
                    classification,
                    MapLoginWithAccessToken(result));
            }

            if (string.IsNullOrEmpty(ok.Data.AuthorizationCode))
            {
                return AutoLoginOutcome.Failed(
                    AutoLoginStep.RestoreWithAccessToken, new HiveError(
                        HiveErrorCode.Internal,
                        "The restore succeeded without an authorization code, so no token could be issued."));
            }

            // The server names the player the token belongs to; the stored session names the player
            // it was saved for. A restore may only revive the second one, so a mismatch stops here,
            // before any token is exchanged — nothing installed, and what the app holds left
            // untouched for it to replace through a fresh sign-in.
            if (ok.Data.PlayerId != stored.PlayerId)
            {
                return AutoLoginOutcome.Failed(
                    AutoLoginStep.RestoreWithAccessToken, new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "The stored access token belongs to a different player than the stored "
                        + "session names. Nothing was installed; have the player sign in."));
            }

            var setup = await SessionSetup.EstablishAsync(
                tokens, session, m_clientId, ok.Data.AuthorizationCode, pkce.Verifier,
                ok.Data.PlayerId, cancellationToken);

            // The setup may just have installed the session, and a caller who canceled is never
            // told Success. Clearing when nothing was installed is a no-op that fires no event.
            if (cancellationToken.IsCancellationRequested)
            {
                session.ClearSession();
                return Canceled(AutoLoginStep.SessionSetup);
            }

            switch (setup.Status)
            {
                case SessionSetupStatus.Established:
                    return AutoLoginOutcome.Succeeded(
                        ok.Data.PlayerId, ok.Data.IsBlock);

                case SessionSetupStatus.TokenCallFailed:
                    return Translate(
                        AutoLoginStep.SessionSetup, setup.Classification,
                        MapIssueToken(setup.TokenResult));

                default:
                    return AutoLoginOutcome.Failed(AutoLoginStep.SessionSetup, setup.Problem);
            }
        }

        /// <summary>
        /// Exchanges the stored refresh token once. Does not retry.
        /// </summary>
        private async Task<AutoLoginOutcome> RefreshAsync(
            ITokenService tokens,
            ISessionManager session,
            StoredSession stored,
            CancellationToken cancellationToken)
        {
            var result = await tokens.IssueTokenAsync(
                new RefreshTokenTokenRequest
                {
                    GrantType = "refresh_token",
                    ClientId = m_clientId,
                    RefreshToken = stored.RefreshToken,
                },
                new ApiCallContext { Token = cancellationToken });

            // Nothing was installed on this path, so there is nothing to tear down — but a session
            // must not appear after the caller asked to stop.
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(AutoLoginStep.RefreshToken);
            }

            var classification = SdkResultClassification.Of(result);
            if (classification.Kind != SdkResultKind.Success
                || !(result is TokenIssueTokenResult.Success ok))
            {
                return Translate(AutoLoginStep.RefreshToken, classification, MapIssueToken(result));
            }

            var accessToken = ok.Data.AccessToken;
            if (string.IsNullOrEmpty(accessToken))
            {
                return AutoLoginOutcome.Failed(AutoLoginStep.RefreshToken, new HiveError(
                    HiveErrorCode.Internal, "The refresh returned no access token."));
            }

            // Require both tokens before installing a renewable session.
            if (string.IsNullOrEmpty(ok.Data.RefreshToken))
            {
                return AutoLoginOutcome.Failed(AutoLoginStep.RefreshToken, new HiveError(
                    HiveErrorCode.Internal, "The refresh returned no refresh token."));
            }

            // Prefer expiresIn; fall back to the JWT expiry when absent. Saturate the addition.
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiresAtSec = ok.Data.ExpiresIn > 0
                ? (ok.Data.ExpiresIn > long.MaxValue - now ? long.MaxValue : now + ok.Data.ExpiresIn)
                : Jwt.ReadExp(accessToken);
            if (expiresAtSec == 0)
            {
                return AutoLoginOutcome.Failed(AutoLoginStep.RefreshToken, new HiveError(
                    HiveErrorCode.Internal,
                    "The refresh carries no expiresIn and the access token no readable exp claim."));
            }

            session.SetSession(accessToken, ok.Data.RefreshToken, stored.PlayerId, expiresAtSec);

            // No block status is available on this route. Query player status separately if needed.
            return AutoLoginOutcome.Succeeded(stored.PlayerId, isBlocked: null);
        }

        /// <summary>
        /// Whether the stored access token is worth spending a round trip on.
        /// </summary>
        private static bool AccessTokenIsUsable(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return false;
            }

            // ReadExp yields 0 for a token whose exp claim is missing or unreadable. Unknown is not
            // the same as valid, so it takes the refresh route rather than being sent as a guess.
            var expiresAtSec = Jwt.ReadExp(accessToken);
            if (expiresAtSec == 0)
            {
                return false;
            }

            return expiresAtSec - k_ExpirySkewSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        /// <summary>
        /// Turns a classified SDK result into this Recipe's outcome. Only the
        /// <see cref="SdkResultKind.TypedOutcome"/> branch consults
        /// <paramref name="businessOutcome"/>; the rest carry everything they need already.
        /// </summary>
        private static AutoLoginOutcome Translate(
            AutoLoginStep step,
            SdkResultClassification classification,
            AutoLoginBusinessOutcome businessOutcome)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UntypedProblem:
                    return AutoLoginOutcome.Failed(step, classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    return AutoLoginOutcome.Unrecognized(
                        step, classification.UnknownCode, classification.RawJson);

                default:
                    // A typed Outcome this Recipe has no value for stays unrecognized rather than
                    // being folded into a neighbouring meaning.
                    return businessOutcome == AutoLoginBusinessOutcome.Unrecognized
                        ? AutoLoginOutcome.Unrecognized(step, string.Empty, classification.RawJson)
                        : AutoLoginOutcome.Business(
                            step, businessOutcome, classification.RawJson,
                            CredentialIsSpent(businessOutcome));
            }
        }

        /// <summary>
        /// Reports caller cancellation without declaring the stored credential invalid.
        /// A refresh request may already have consumed the token before cancellation.
        /// </summary>
        private static AutoLoginOutcome Canceled(AutoLoginStep step) =>
            AutoLoginOutcome.Failed(step, new HiveError(
                HiveErrorCode.Cancelled, "The auto login was canceled."));

        /// <summary>
        /// Whether this refusal means the stored credential itself is finished.
        /// </summary>
        private static bool CredentialIsSpent(AutoLoginBusinessOutcome businessOutcome) =>
            businessOutcome == AutoLoginBusinessOutcome.PlayerNotFound
            || businessOutcome == AutoLoginBusinessOutcome.InvalidRefreshToken;

        private static AutoLoginBusinessOutcome MapLoginWithAccessToken(
            AuthLoginWithAccessTokenResult result)
        {
            switch (result)
            {
                case AuthLoginWithAccessTokenResult.AppIdMismatch _:
                    return AutoLoginBusinessOutcome.AppIdMismatch;
                case AuthLoginWithAccessTokenResult.InvalidGatewayContext _:
                    return AutoLoginBusinessOutcome.InvalidGatewayContext;
                case AuthLoginWithAccessTokenResult.TerminateService _:
                    return AutoLoginBusinessOutcome.ServiceTerminated;
                case AuthLoginWithAccessTokenResult.InvalidClientId _:
                    return AutoLoginBusinessOutcome.InvalidClient;
                case AuthLoginWithAccessTokenResult.IpBlocked _:
                    return AutoLoginBusinessOutcome.IpBlocked;
                case AuthLoginWithAccessTokenResult.AppNotFound _:
                    return AutoLoginBusinessOutcome.AppNotFound;
                case AuthLoginWithAccessTokenResult.PlayerNotFound _:
                    return AutoLoginBusinessOutcome.PlayerNotFound;
                default:
                    return AutoLoginBusinessOutcome.Unrecognized;
            }
        }

        private static AutoLoginBusinessOutcome MapIssueToken(TokenIssueTokenResult result)
        {
            switch (result)
            {
                case TokenIssueTokenResult.InvalidClient _:
                    return AutoLoginBusinessOutcome.InvalidClient;
                case TokenIssueTokenResult.UnsupportedGrantType _:
                    return AutoLoginBusinessOutcome.UnsupportedGrantType;
                case TokenIssueTokenResult.InvalidGrantExpired _:
                    return AutoLoginBusinessOutcome.ExpiredAuthorizationCode;
                case TokenIssueTokenResult.InvalidGrantCodeChallenge _:
                    return AutoLoginBusinessOutcome.CodeChallengeMismatch;
                case TokenIssueTokenResult.InvalidGrantRefreshToken _:
                    return AutoLoginBusinessOutcome.InvalidRefreshToken;
                case TokenIssueTokenResult.InvalidGrant _:
                    // Checked after the more specific invalid_grant variants above.
                    return AutoLoginBusinessOutcome.InvalidAuthorizationCode;
                case TokenIssueTokenResult.AppNotFound _:
                    return AutoLoginBusinessOutcome.AppNotFound;
                case TokenIssueTokenResult.TemporarilyUnavailable _:
                    return AutoLoginBusinessOutcome.TemporarilyUnavailable;
                default:
                    return AutoLoginBusinessOutcome.Unrecognized;
            }
        }
    }
}

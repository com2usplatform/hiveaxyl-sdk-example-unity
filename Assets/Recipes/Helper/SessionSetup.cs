// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>How the shared session-setup step ended.</summary>
    internal enum SessionSetupStatus
    {
        /// <summary>Tokens were issued and the session is live.</summary>
        Established,

        /// <summary>The token call itself did not succeed; see the classified SDK result.</summary>
        TokenCallFailed,

        /// <summary>The token call succeeded but returned something no session can be built from.</summary>
        UnusableTokenResponse,

        /// <summary>
        /// The caller cancelled while the tokens were being fetched, and no session was installed.
        /// </summary>
        Cancelled,
    }

    /// <summary>The result of the shared session-setup step.</summary>
    internal readonly struct SessionSetupResult
    {
        private SessionSetupResult(
            SessionSetupStatus status,
            SdkResultClassification classification,
            TokenIssueTokenResult tokenResult,
            HiveError problem)
        {
            Status = status;
            Classification = classification;
            TokenResult = tokenResult;
            Problem = problem;
        }

        /// <summary>How the step ended.</summary>
        public SessionSetupStatus Status { get; }

        /// <summary>
        /// The classified token-call result. Meaningful only for
        /// <see cref="SessionSetupStatus.TokenCallFailed"/>, where the caller maps it the same way it
        /// maps its own SDK calls.
        /// </summary>
        public SdkResultClassification Classification { get; }

        /// <summary>
        /// The token call's own result, so the caller can translate its typed Outcome variants into
        /// Recipe values. Non-null only for <see cref="SessionSetupStatus.TokenCallFailed"/>.
        /// </summary>
        public TokenIssueTokenResult TokenResult { get; }

        /// <summary>
        /// The error for <see cref="SessionSetupStatus.UnusableTokenResponse"/>. Null for other statuses.
        /// </summary>
        public HiveError Problem { get; }

        /// <summary>The session is live.</summary>
        internal static SessionSetupResult Established() =>
            new SessionSetupResult(SessionSetupStatus.Established, default, null, null);

        /// <summary>The caller cancelled before the session was installed.</summary>
        internal static SessionSetupResult Cancelled() =>
            new SessionSetupResult(SessionSetupStatus.Cancelled, default, null, null);

        /// <summary>The token call returned a non-success result.</summary>
        internal static SessionSetupResult TokenCallFailed(
            SdkResultClassification classification, TokenIssueTokenResult tokenResult) =>
            new SessionSetupResult(
                SessionSetupStatus.TokenCallFailed, classification, tokenResult, null);

        /// <summary>The token call succeeded but no session could be built from what it returned.</summary>
        internal static SessionSetupResult UnusableTokenResponse(HiveError problem) =>
            new SessionSetupResult(SessionSetupStatus.UnusableTokenResponse, default, null, problem);
    }

    /// <summary>
    /// Exchanges an authorization code for tokens and installs the session when they are usable.
    /// </summary>
    internal static class SessionSetup
    {
        /// <summary>
        /// Exchanges <paramref name="authorizationCode"/> for tokens and, when they are usable,
        /// installs the session.
        /// </summary>
        /// <param name="tokens">The token Capability. Must not be null.</param>
        /// <param name="session">Core's session state. Must not be null.</param>
        /// <param name="clientId">The PKCE client id the authorization code was issued for.</param>
        /// <param name="authorizationCode">The one-time code the login call returned.</param>
        /// <param name="codeVerifier">The PKCE verifier whose challenge started the login.</param>
        /// <param name="playerId">The player the login call resolved.</param>
        /// <param name="cancellationToken">The caller's token, forwarded to the token call.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="tokens"/> or <paramref name="session"/> is null.
        /// </exception>
        internal static async Task<SessionSetupResult> EstablishAsync(
            ITokenService tokens,
            ISessionManager session,
            string clientId,
            string authorizationCode,
            string codeVerifier,
            long playerId,
            CancellationToken cancellationToken)
        {
            if (tokens == null)
            {
                throw new ArgumentNullException(nameof(tokens));
            }

            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var request = new AuthorizationCodeTokenRequest
            {
                ClientId = clientId,
                AuthorizationCode = authorizationCode,
                CodeVerifier = codeVerifier,
            };

            var result = await tokens.IssueTokenAsync(
                request, new ApiCallContext { Token = cancellationToken });

            var classification = SdkResultClassification.Of(result);
            if (classification.Kind != SdkResultKind.Success)
            {
                return SessionSetupResult.TokenCallFailed(classification, result);
            }

            // Only the Success variant's constructor sets the success flag, so this holds; report it
            // as an unusable response rather than throwing out of a Recipe step if it ever does not.
            if (!(result is TokenIssueTokenResult.Success success))
            {
                return SessionSetupResult.UnusableTokenResponse(new HiveError(
                    HiveErrorCode.Internal, "Token call reported success without a success payload."));
            }

            var accessToken = success.Data.AccessToken;
            var refreshToken = success.Data.RefreshToken;

            if (string.IsNullOrEmpty(accessToken))
            {
                return SessionSetupResult.UnusableTokenResponse(new HiveError(
                    HiveErrorCode.Internal, "Token response carried no access token."));
            }

            if (string.IsNullOrEmpty(refreshToken))
            {
                return SessionSetupResult.UnusableTokenResponse(new HiveError(
                    HiveErrorCode.Internal, "Token response carried no refresh token."));
            }

            // Prefer expiresIn; fall back to the JWT expiry when absent.
            // Reject unknown expiry and saturate the addition.
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiresAtSec = success.Data.ExpiresIn > 0
                ? (success.Data.ExpiresIn > long.MaxValue - now
                    ? long.MaxValue
                    : now + success.Data.ExpiresIn)
                : Jwt.ReadExp(accessToken);
            if (expiresAtSec == 0)
            {
                return SessionSetupResult.UnusableTokenResponse(new HiveError(
                    HiveErrorCode.Internal,
                    "The response carries no expiresIn and the access token no readable exp claim."));
            }

            // Re-check cancellation immediately before installing the session.
            if (cancellationToken.IsCancellationRequested)
            {
                return SessionSetupResult.Cancelled();
            }

            session.SetSession(accessToken, refreshToken, playerId, expiresAtSec);
            return SessionSetupResult.Established();
        }
    }
}

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of one Custom login attempt.</summary>
    public enum LoginWithCustomStatus
    {
        /// <summary>The player is logged in and the session is live.</summary>
        Success,

        /// <summary>The server answered with a non-success Outcome. See <c>BusinessOutcome</c>.</summary>
        BusinessOutcome,

        /// <summary>A technical failure. See <c>Error</c> for the preserved <see cref="HiveError"/>.</summary>
        Failure,
    }

    /// <summary>Which step of the attempt did not complete.</summary>
    public enum LoginWithCustomStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Checking the grant key the caller supplied, before any call is made.</summary>
        Validate,

        /// <summary>Resolving the SDK services the login needs.</summary>
        Resolve,

        /// <summary>Redeeming the grant key for an authorization code.</summary>
        CustomLogin,

        /// <summary>Exchanging the authorization code for tokens and installing the session.</summary>
        SessionSetup,
    }

    /// <summary>
    /// The attempt's business results, translated from the SDK Outcomes of every step into one
    /// vocabulary. Branch on these values, never on a message string.
    /// </summary>
    public enum LoginWithCustomBusinessOutcome
    {
        /// <summary>
        /// An Outcome this Recipe does not translate. Two cases, told apart by
        /// <see cref="LoginWithCustomOutcome.UnknownOutcomeCode"/>: non-empty means the server sent a
        /// code this SDK build does not know; empty means the SDK typed the Outcome but this Recipe
        /// has no value for it yet. Never guess a known value from either.
        /// </summary>
        Unrecognized = 0,

        /// <summary>
        /// The grant key was rejected. Obtain a fresh key from the game's server before trying again.
        /// </summary>
        InvalidGrantKey,

        /// <summary>The service has been terminated for this app.</summary>
        ServiceTerminated,

        /// <summary>The client id is not one this app may use.</summary>
        InvalidClient,

        /// <summary>The caller's IP is blocked.</summary>
        IpBlocked,

        /// <summary>The app id is unknown to the server.</summary>
        AppNotFound,

        /// <summary>
        /// Token issuance is temporarily unavailable after the grant key was redeemed.
        /// To start a new login attempt, obtain a fresh grant key from the game's server.
        /// Do not repeat LoginWithCustomAsync with the redeemed key.
        /// </summary>
        TemporarilyUnavailable,

        /// <summary>The provider the grant key was issued for is not configured for this app.</summary>
        ProviderConfigNotFound,

        /// <summary>The provider the grant key was issued for is not one the server supports.</summary>
        ProviderNotSupported,

        /// <summary>The token endpoint does not support the requested grant type.</summary>
        UnsupportedGrantType,

        /// <summary>The authorization code was rejected.</summary>
        InvalidAuthorizationCode,

        /// <summary>The authorization code had already expired.</summary>
        ExpiredAuthorizationCode,

        /// <summary>The PKCE verifier did not match the challenge the login started with.</summary>
        CodeChallengeMismatch,

        /// <summary>The refresh token was rejected.</summary>
        InvalidRefreshToken,
    }

    /// <summary>
    /// The result of one Custom login attempt, whichever step it ended at. Read
    /// <see cref="Status"/> first; the other members are meaningful per the state documented on each.
    /// </summary>
    public sealed class LoginWithCustomOutcome
    {
        private LoginWithCustomOutcome(
            LoginWithCustomStatus status,
            LoginWithCustomStep failedStep,
            long playerId,
            bool isBlocked,
            LoginWithCustomBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            PlayerId = playerId;
            IsBlocked = isBlocked;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public LoginWithCustomStatus Status { get; }

        /// <summary>
        /// The step that did not complete, or <see cref="LoginWithCustomStep.None"/> on success.
        /// Report it in diagnostics; do not branch game logic on it.
        /// </summary>
        public LoginWithCustomStep FailedStep { get; }

        /// <summary>
        /// The player now signed in. Meaningful only on <see cref="LoginWithCustomStatus.Success"/>.
        /// </summary>
        public long PlayerId { get; }

        /// <summary>Whether the server reports this player as blocked.</summary>
        public bool IsBlocked { get; }

        /// <summary>
        /// The translated business result. Meaningful only on
        /// <see cref="LoginWithCustomStatus.BusinessOutcome"/>.
        /// </summary>
        public LoginWithCustomBusinessOutcome BusinessOutcome { get; }

        /// <summary>
        /// Whether the server explicitly rejected the grant key.
        /// Obtain a fresh key from the game's server when true.
        /// </summary>
        /// <remarks>
        /// False does not guarantee that the supplied key can be reused.
        /// The key may already have been redeemed before token exchange failed or
        /// cancellation occurred.
        /// </remarks>
        public bool IsGrantKeyRejected =>
            Status == LoginWithCustomStatus.BusinessOutcome
            && BusinessOutcome == LoginWithCustomBusinessOutcome.InvalidGrantKey;

        /// <summary>
        /// The server's Outcome code when this SDK build did not recognize it. Empty otherwise.
        /// Diagnostics only.
        /// </summary>
        public string UnknownOutcomeCode { get; }

        /// <summary>
        /// The unparsed response body, kept for forward-compatibility diagnosis only. Never branch on
        /// it and never show it to a player.
        /// </summary>
        public string RawJson { get; }

        /// <summary>
        /// The preserved SDK error. Non-null only on <see cref="LoginWithCustomStatus.Failure"/>.
        /// </summary>
        public HiveError Error { get; }

        /// <summary>The player is logged in.</summary>
        internal static LoginWithCustomOutcome Succeeded(long playerId, bool isBlocked) =>
            new LoginWithCustomOutcome(
                LoginWithCustomStatus.Success, LoginWithCustomStep.None, playerId, isBlocked,
                LoginWithCustomBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        /// <summary>The server answered with an Outcome this Recipe translates.</summary>
        internal static LoginWithCustomOutcome Business(
            LoginWithCustomStep failedStep,
            LoginWithCustomBusinessOutcome businessOutcome,
            string rawJson) =>
            new LoginWithCustomOutcome(
                LoginWithCustomStatus.BusinessOutcome, failedStep, 0, false, businessOutcome,
                string.Empty, rawJson ?? string.Empty, null);

        /// <summary>
        /// The server answered with an Outcome that is not translated — either unknown to this SDK
        /// build (<paramref name="unknownOutcomeCode"/> non-empty) or unmapped by this Recipe.
        /// </summary>
        internal static LoginWithCustomOutcome Unrecognized(
            LoginWithCustomStep failedStep, string unknownOutcomeCode, string rawJson) =>
            new LoginWithCustomOutcome(
                LoginWithCustomStatus.BusinessOutcome, failedStep, 0, false,
                LoginWithCustomBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>A technical failure; the SDK error is preserved as given.</summary>
        internal static LoginWithCustomOutcome Failed(
            LoginWithCustomStep failedStep, HiveError error) =>
            new LoginWithCustomOutcome(
                LoginWithCustomStatus.Failure, failedStep, 0, false,
                LoginWithCustomBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

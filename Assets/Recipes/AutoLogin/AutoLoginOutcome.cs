// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of an auto-login attempt.</summary>
    public enum AutoLoginStatus
    {
        /// <summary>The session was restored and is live.</summary>
        Success,

        /// <summary>
        /// There was nothing to restore from. A first launch, or a player who signed out — the
        /// normal way to arrive at a login screen, not a failure to report.
        /// </summary>
        NoSession,

        /// <summary>The server answered with a non-success Outcome. See <c>BusinessOutcome</c>.</summary>
        BusinessOutcome,

        /// <summary>A technical failure. See <c>Error</c> for the preserved <see cref="HiveError"/>.</summary>
        Failure,
    }

    /// <summary>Which step of the auto-login did not complete.</summary>
    public enum AutoLoginStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Resolving the SDK services the restore needs.</summary>
        Resolve,

        /// <summary>Restoring from the stored access token.</summary>
        RestoreWithAccessToken,

        /// <summary>Trading the stored refresh token for a new pair.</summary>
        RefreshToken,

        /// <summary>Exchanging the authorization code for tokens and installing the session.</summary>
        SessionSetup,
    }

    /// <summary>
    /// The auto-login's business results, translated from the SDK Outcomes of every step into one
    /// vocabulary. Branch on these values, never on a message string.
    /// </summary>
    public enum AutoLoginBusinessOutcome
    {
        /// <summary>
        /// An Outcome this Recipe does not translate. Two cases, told apart by
        /// <see cref="AutoLoginOutcome.UnknownOutcomeCode"/>: non-empty means the server sent a code
        /// this SDK build does not know; empty means the SDK typed the Outcome but this Recipe has
        /// no value for it yet. Never guess a known value from either.
        /// </summary>
        Unrecognized = 0,

        /// <summary>The stored token belongs to a different app id than this build's.</summary>
        AppIdMismatch,

        /// <summary>The gateway rejected the call's routing context.</summary>
        InvalidGatewayContext,

        /// <summary>The service has been terminated for this app.</summary>
        ServiceTerminated,

        /// <summary>The client id is not one this app may use.</summary>
        InvalidClient,

        /// <summary>The caller's IP is blocked.</summary>
        IpBlocked,

        /// <summary>The app id is unknown to the server.</summary>
        AppNotFound,

        /// <summary>
        /// Token issuance is temporarily unavailable.
        /// </summary>
        TemporarilyUnavailable,

        /// <summary>The stored tokens name a player the server does not have.</summary>
        PlayerNotFound,

        /// <summary>The stored refresh token was rejected. Nothing is left to restore from.</summary>
        InvalidRefreshToken,

        /// <summary>The token endpoint does not support the requested grant type.</summary>
        UnsupportedGrantType,

        /// <summary>The authorization code was rejected.</summary>
        InvalidAuthorizationCode,

        /// <summary>The authorization code had already expired.</summary>
        ExpiredAuthorizationCode,

        /// <summary>The PKCE verifier did not match the challenge the restore started with.</summary>
        CodeChallengeMismatch,
    }

    /// <summary>
    /// The result of one auto-login attempt. Read <see cref="Status"/> first, then
    /// <see cref="StoredCredentialIsStale"/> — the second says whether what the app has on disk is
    /// still worth keeping, which <see cref="Status"/> does not answer.
    /// </summary>
    public sealed class AutoLoginOutcome
    {
        private AutoLoginOutcome(
            AutoLoginStatus status,
            AutoLoginStep failedStep,
            long playerId,
            bool? isBlocked,
            bool storedCredentialIsStale,
            AutoLoginBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            PlayerId = playerId;
            IsBlocked = isBlocked;
            StoredCredentialIsStale = storedCredentialIsStale;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public AutoLoginStatus Status { get; }

        /// <summary>
        /// The step that did not complete, or <see cref="AutoLoginStep.None"/> on success. Report it
        /// in diagnostics; do not branch game logic on it.
        /// </summary>
        public AutoLoginStep FailedStep { get; }

        /// <summary>
        /// The restored player. Meaningful only on <see cref="AutoLoginStatus.Success"/>.
        /// </summary>
        public long PlayerId { get; }

        /// <summary>
        /// The SDK response's block flag after successful access-token login.
        /// Null after successful refresh-token login and on all non-success outcomes.
        /// </summary>
        /// <remarks>
        /// The SDK represents this flag as bool and defaults it to false when the response
        /// omits it. This property cannot distinguish an omitted flag from an explicit false.
        /// </remarks>
        public bool? IsBlocked { get; }

        /// <summary>
        /// Whether the stored credential is known to be unusable and should be discarded.
        /// </summary>
        /// <remarks>
        /// True when the server rejects the stored credential, or when no usable access
        /// token and no refresh token remain. False does not guarantee reuse: a refresh
        /// request may consume its token before its response is lost or canceled.
        /// The Recipe does not modify application storage.
        /// </remarks>
        public bool StoredCredentialIsStale { get; }

        /// <summary>
        /// The translated business result. Meaningful only on
        /// <see cref="AutoLoginStatus.BusinessOutcome"/>.
        /// </summary>
        public AutoLoginBusinessOutcome BusinessOutcome { get; }

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
        /// The preserved SDK error. Non-null only on <see cref="AutoLoginStatus.Failure"/>.
        /// </summary>
        public HiveError Error { get; }

        /// <summary>The session is live.</summary>
        internal static AutoLoginOutcome Succeeded(long playerId, bool? isBlocked) =>
            new AutoLoginOutcome(
                AutoLoginStatus.Success, AutoLoginStep.None, playerId, isBlocked, false,
                AutoLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        /// <summary>
        /// There was nothing to restore from — either nothing stored at all, or a credential with
        /// nothing left in it, which <paramref name="storedCredentialIsStale"/> tells apart.
        /// </summary>
        internal static AutoLoginOutcome NoSession(bool storedCredentialIsStale = false) =>
            new AutoLoginOutcome(
                AutoLoginStatus.NoSession, AutoLoginStep.None, 0, null,
                storedCredentialIsStale, AutoLoginBusinessOutcome.Unrecognized, string.Empty,
                string.Empty, null);

        /// <summary>The server answered with an Outcome this Recipe translates.</summary>
        internal static AutoLoginOutcome Business(
            AutoLoginStep failedStep,
            AutoLoginBusinessOutcome businessOutcome,
            string rawJson,
            bool storedCredentialIsStale) =>
            new AutoLoginOutcome(
                AutoLoginStatus.BusinessOutcome, failedStep, 0, null, storedCredentialIsStale,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        /// <summary>
        /// The server answered with an Outcome that is not translated — either unknown to this SDK
        /// build (<paramref name="unknownOutcomeCode"/> non-empty) or unmapped by this Recipe.
        /// </summary>
        internal static AutoLoginOutcome Unrecognized(
            AutoLoginStep failedStep, string unknownOutcomeCode, string rawJson) =>
            new AutoLoginOutcome(
                AutoLoginStatus.BusinessOutcome, failedStep, 0, null, false,
                AutoLoginBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>A technical failure; the SDK error is preserved as given.</summary>
        internal static AutoLoginOutcome Failed(AutoLoginStep failedStep, HiveError error) =>
            new AutoLoginOutcome(
                AutoLoginStatus.Failure, failedStep, 0, null, false,
                AutoLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

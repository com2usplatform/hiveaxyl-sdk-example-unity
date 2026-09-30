// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of a Guest login attempt.</summary>
    public enum LoginAsGuestStatus
    {
        /// <summary>The player is logged in and the session is live.</summary>
        Success = 0,

        /// <summary>The server answered with a non-success Outcome. See <c>BusinessOutcome</c>.</summary>
        BusinessOutcome = 1,

        // Value 2 is reserved to preserve compatibility with existing serialized values.
        /// <summary>A technical failure. See <c>Error</c> for the preserved <see cref="HiveError"/>.</summary>
        Failure = 3,
    }

    /// <summary>Which step of the Guest login did not complete.</summary>
    public enum LoginAsGuestStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Resolving the SDK services the login needs.</summary>
        Resolve,

        /// <summary>Creating a new Guest account.</summary>
        CreateGuest,

        /// <summary>Logging an existing Guest account back in.</summary>
        RestoreGuest,

        /// <summary>Exchanging the authorization code for tokens and installing the session.</summary>
        SessionSetup,
    }

    /// <summary>
    /// The Guest login's business results, translated from the SDK Outcomes of every step into one
    /// vocabulary. Branch on these values, never on a message string.
    /// </summary>
    public enum LoginAsGuestBusinessOutcome
    {
        /// <summary>
        /// An Outcome this Recipe does not translate. Two cases, told apart by
        /// <see cref="LoginAsGuestOutcome.UnknownOutcomeCode"/>: non-empty means the server sent a
        /// code this SDK build does not know; empty means the SDK typed the Outcome but this Recipe
        /// has no value for it yet. Never guess a known value from either.
        /// </summary>
        Unrecognized = 0,

        /// <summary>The supplied grant key was rejected.</summary>
        InvalidGrantKey,

        /// <summary>The app requires a grant key and the request carried none.</summary>
        GrantKeyRequired,

        /// <summary>
        /// The stored guest token is invalid and cannot be used to sign in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This does not necessarily mean the account is lost. Linking a login provider invalidates
        /// the guest token; the player can then sign in with the linked provider.
        /// Creating a new guest account starts a different account instead of restoring the existing one.
        /// </para>
        /// <para>
        /// After linking succeeds, remove the stored guest credentials whose
        /// <see cref="GuestCredential.PlayerId"/> matches the linked player.
        /// Use the linked player's ID, since the current session may have changed during the request.
        /// </para>
        /// </remarks>
        InvalidGuestToken,

        /// <summary>The service has been terminated for this app.</summary>
        ServiceTerminated,

        /// <summary>The client id is not one this app may use.</summary>
        InvalidClient,

        /// <summary>The caller's IP is blocked.</summary>
        IpBlocked,

        /// <summary>The console has no configuration for this provider.</summary>
        ProviderConfigNotFound,

        /// <summary>The app id is unknown to the server.</summary>
        AppNotFound,

        /// <summary>
        /// Token issuance is temporarily unavailable. Keep the returned Guest credential and use it
        /// to restore the account on a later attempt; do not repeat the new-account creation flow.
        /// </summary>
        TemporarilyUnavailable,

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
    /// The result of one Guest login attempt, whichever step it ended at. Read <see cref="Status"/>
    /// first; the other members are meaningful per the state documented on each.
    /// </summary>
    public sealed class LoginAsGuestOutcome
    {
        private LoginAsGuestOutcome(
            LoginAsGuestStatus status,
            LoginAsGuestStep failedStep,
            GuestCredential credential,
            bool isNewAccount,
            bool isBlocked,
            LoginAsGuestBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            Credential = credential;
            IsNewAccount = isNewAccount;
            IsBlocked = isBlocked;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public LoginAsGuestStatus Status { get; }

        /// <summary>
        /// The step that did not complete, or <see cref="LoginAsGuestStep.None"/> on success. Report
        /// it in diagnostics; do not branch game logic on it.
        /// </summary>
        public LoginAsGuestStep FailedStep { get; }

        /// <summary>
        /// The credential to persist for the next launch. On a restore it is the credential that was
        /// passed in, unchanged.
        /// </summary>
        /// <remarks>
        /// Persist this credential whenever it is non-null, including after failure or cancellation.
        /// It may be the only way to recover the account. Read <see cref="Status"/> separately.
        /// </remarks>
        public GuestCredential Credential { get; }

        /// <summary>
        /// <c>true</c> when this login created a new Guest account rather than restoring one.
        /// Meaningful only on <see cref="LoginAsGuestStatus.Success"/>.
        /// </summary>
        public bool IsNewAccount { get; }

        /// <summary>
        /// Whether the server reports this player as blocked. Reported on the restore path only, so
        /// it is always <c>false</c> right after a Guest account is created.
        /// </summary>
        public bool IsBlocked { get; }

        /// <summary>
        /// The translated business result. Meaningful only on
        /// <see cref="LoginAsGuestStatus.BusinessOutcome"/>.
        /// </summary>
        public LoginAsGuestBusinessOutcome BusinessOutcome { get; }

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
        /// The preserved SDK error. Non-null only on <see cref="LoginAsGuestStatus.Failure"/>.
        /// </summary>
        public HiveError Error { get; }

        /// <summary>The player is logged in.</summary>
        internal static LoginAsGuestOutcome Succeeded(
            GuestCredential credential, bool isNewAccount, bool isBlocked) =>
            new LoginAsGuestOutcome(
                LoginAsGuestStatus.Success, LoginAsGuestStep.None, credential, isNewAccount,
                isBlocked, LoginAsGuestBusinessOutcome.Unrecognized, string.Empty,
                string.Empty, null);

        /// <summary>The server answered with an Outcome this Recipe translates.</summary>
        internal static LoginAsGuestOutcome Business(
            LoginAsGuestStep failedStep,
            LoginAsGuestBusinessOutcome businessOutcome,
            string rawJson,
            GuestCredential credential = null) =>
            new LoginAsGuestOutcome(
                LoginAsGuestStatus.BusinessOutcome, failedStep, credential, false, false,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        /// <summary>
        /// The server answered with an Outcome that is not translated — either unknown to this SDK
        /// build (<paramref name="unknownOutcomeCode"/> non-empty) or unmapped by this Recipe.
        /// </summary>
        internal static LoginAsGuestOutcome Unrecognized(
            LoginAsGuestStep failedStep,
            string unknownOutcomeCode,
            string rawJson,
            GuestCredential credential = null) =>
            new LoginAsGuestOutcome(
                LoginAsGuestStatus.BusinessOutcome, failedStep, credential, false, false,
                LoginAsGuestBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>A technical failure; the SDK error is preserved as given.</summary>
        internal static LoginAsGuestOutcome Failed(
            LoginAsGuestStep failedStep, HiveError error, GuestCredential credential = null) =>
            new LoginAsGuestOutcome(
                LoginAsGuestStatus.Failure, failedStep, credential, false, false,
                LoginAsGuestBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

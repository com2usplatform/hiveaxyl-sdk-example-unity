// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of one Username attempt, whichever of its two calls it ended at.</summary>
    public enum LoginWithUsernameStatus
    {
        /// <summary>The player is logged in and the session is live.</summary>
        Success,

        /// <summary>The server answered with a non-success Outcome. See <c>BusinessOutcome</c>.</summary>
        BusinessOutcome,

        /// <summary>A technical failure. See <c>Error</c> for the preserved <see cref="HiveError"/>.</summary>
        Failure,
    }

    /// <summary>Which step of the attempt did not complete.</summary>
    public enum LoginWithUsernameStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Checking the username and password the caller supplied, before any call is made.</summary>
        Validate,

        /// <summary>Resolving the SDK services the login needs.</summary>
        Resolve,

        /// <summary>Creating the Username account.</summary>
        SignUp,

        /// <summary>Signing an existing Username account in.</summary>
        LogIn,

        /// <summary>Exchanging the authorization code for tokens and installing the session.</summary>
        SessionSetup,
    }

    /// <summary>
    /// The attempt's business results, translated from the SDK Outcomes of every step into one
    /// vocabulary. Branch on these values, never on a message string.
    /// </summary>
    public enum LoginWithUsernameBusinessOutcome
    {
        /// <summary>
        /// An Outcome this Recipe does not translate. Two cases, told apart by
        /// <see cref="LoginWithUsernameOutcome.UnknownOutcomeCode"/>: non-empty means the server sent
        /// a code this SDK build does not know; empty means the SDK typed the Outcome but this Recipe
        /// has no value for it yet. Never guess a known value from either.
        /// </summary>
        Unrecognized = 0,

        /// <summary>
        /// The username or password is incorrect. Ask the player to re-enter both.
        /// </summary>
        UsernameOrPasswordIncorrect,

        /// <summary>The supplied grant key was rejected.</summary>
        InvalidGrantKey,

        /// <summary>
        /// The app has security hardening enabled, so creating the account requires a grant key and
        /// none was passed. Grant keys are issued to the game's own server rather than to the
        /// client; fetch one and hand it to <c>LogInOrSignUpAsync</c>.
        /// </summary>
        GrantKeyRequired,

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

        /// <summary>The Username provider is not configured for this app.</summary>
        ProviderConfigNotFound,

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
    /// The result of one Username attempt, whichever step it ended at. Read <see cref="Status"/>
    /// first; the other members are meaningful per the state documented on each.
    /// </summary>
    public sealed class LoginWithUsernameOutcome
    {
        private LoginWithUsernameOutcome(
            LoginWithUsernameStatus status,
            LoginWithUsernameStep failedStep,
            long playerId,
            bool isNewAccount,
            bool isBlocked,
            LoginWithUsernameBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            PlayerId = playerId;
            IsNewAccount = isNewAccount;
            IsBlocked = isBlocked;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public LoginWithUsernameStatus Status { get; }

        /// <summary>
        /// The step that did not complete, or <see cref="LoginWithUsernameStep.None"/> on success.
        /// Report it in diagnostics; do not branch game logic on it.
        /// </summary>
        public LoginWithUsernameStep FailedStep { get; }

        /// <summary>
        /// The player now signed in. Meaningful only on <see cref="LoginWithUsernameStatus.Success"/>,
        /// and worth comparing against the player the app remembers: a sign-in does not always land on
        /// the account it landed on last time. See <see cref="IsNewAccount"/>.
        /// </summary>
        public long PlayerId { get; }

        /// <summary>
        /// <c>true</c> when this attempt created the account rather than signing in to an existing
        /// one. Meaningful only on <see cref="LoginWithUsernameStatus.Success"/>.
        /// </summary>
        /// <remarks>
        /// Inform the player when an account was created, including after a mistyped username.
        /// Compare <see cref="PlayerId"/> with the stored player before continuing account-specific work.
        /// </remarks>
        public bool IsNewAccount { get; }

        /// <summary>
        /// Whether the server reports this player as blocked. Reported on the sign-in path only, so it
        /// is always <c>false</c> right after an account is created.
        /// </summary>
        public bool IsBlocked { get; }

        /// <summary>
        /// The translated business result. Meaningful only on
        /// <see cref="LoginWithUsernameStatus.BusinessOutcome"/>.
        /// </summary>
        public LoginWithUsernameBusinessOutcome BusinessOutcome { get; }

        /// <summary>
        /// True when the result is UsernameOrPasswordIncorrect.
        /// The app can use this to ask the player to re-enter their username or password.
        /// </summary>
        public bool IsUsernameOrPasswordIncorrect =>
            Status == LoginWithUsernameStatus.BusinessOutcome
            && BusinessOutcome == LoginWithUsernameBusinessOutcome.UsernameOrPasswordIncorrect;

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
        /// The preserved SDK error. Non-null only on <see cref="LoginWithUsernameStatus.Failure"/>.
        /// </summary>
        public HiveError Error { get; }

        /// <summary>The player is logged in.</summary>
        internal static LoginWithUsernameOutcome Succeeded(
            long playerId, bool isNewAccount, bool isBlocked) =>
            new LoginWithUsernameOutcome(
                LoginWithUsernameStatus.Success, LoginWithUsernameStep.None, playerId, isNewAccount,
                isBlocked, LoginWithUsernameBusinessOutcome.Unrecognized, string.Empty,
                string.Empty, null);

        /// <summary>The server answered with an Outcome this Recipe translates.</summary>
        internal static LoginWithUsernameOutcome Business(
            LoginWithUsernameStep failedStep,
            LoginWithUsernameBusinessOutcome businessOutcome,
            string rawJson) =>
            new LoginWithUsernameOutcome(
                LoginWithUsernameStatus.BusinessOutcome, failedStep, 0, false, false,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        /// <summary>
        /// The server answered with an Outcome that is not translated — either unknown to this SDK
        /// build (<paramref name="unknownOutcomeCode"/> non-empty) or unmapped by this Recipe.
        /// </summary>
        internal static LoginWithUsernameOutcome Unrecognized(
            LoginWithUsernameStep failedStep, string unknownOutcomeCode, string rawJson) =>
            new LoginWithUsernameOutcome(
                LoginWithUsernameStatus.BusinessOutcome, failedStep, 0, false, false,
                LoginWithUsernameBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>A technical failure; the SDK error is preserved as given.</summary>
        internal static LoginWithUsernameOutcome Failed(
            LoginWithUsernameStep failedStep, HiveError error) =>
            new LoginWithUsernameOutcome(
                LoginWithUsernameStatus.Failure, failedStep, 0, false, false,
                LoginWithUsernameBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

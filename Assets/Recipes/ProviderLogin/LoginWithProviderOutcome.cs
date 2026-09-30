// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of a provider login attempt.</summary>
    public enum LoginWithProviderStatus
    {
        /// <summary>The player is logged in and the session is live.</summary>
        Success,

        /// <summary>The server answered with a non-success Outcome.</summary>
        BusinessOutcome,

        /// <summary>The user dismissed the provider's UI, or declined at its consent screen.</summary>
        UserCanceled,

        /// <summary>A technical failure. See <c>Error</c> for the preserved <see cref="HiveError"/>.</summary>
        Failure,
    }

    /// <summary>Which step of the provider login did not complete.</summary>
    public enum LoginWithProviderStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Resolving the SDK services the login needs.</summary>
        Resolve,

        /// <summary>Obtaining the credential from the provider.</summary>
        AcquireCredential,

        /// <summary>Handing that credential to the Axyl server.</summary>
        LoginProvider,

        /// <summary>Exchanging the authorization code for tokens and installing the session.</summary>
        SessionSetup,
    }

    /// <summary>
    /// The result of one provider login, whichever provider it used and whichever step it ended at.
    /// Read <see cref="Status"/> first; the other members are meaningful per the state documented on
    /// each.
    /// </summary>
    public sealed class LoginWithProviderOutcome
    {
        private LoginWithProviderOutcome(
            LoginWithProviderStatus status,
            LoginWithProviderStep failedStep,
            LoginProvider provider,
            long playerId,
            bool isBlocked,
            ProviderLoginBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            Provider = provider;
            PlayerId = playerId;
            IsBlocked = isBlocked;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public LoginWithProviderStatus Status { get; }

        /// <summary>
        /// The step that did not complete, or <see cref="LoginWithProviderStep.None"/> on success.
        /// Report it in diagnostics; do not branch game logic on it.
        /// </summary>
        public LoginWithProviderStep FailedStep { get; }

        /// <summary>The provider the attempt used.</summary>
        public LoginProvider Provider { get; }

        /// <summary>The player that was logged in. Set only on <see cref="LoginWithProviderStatus.Success"/>.</summary>
        public long PlayerId { get; }

        /// <summary>Whether the server reports this player as blocked. Set only on success.</summary>
        public bool IsBlocked { get; }

        /// <summary>
        /// The translated business result. Meaningful only on
        /// <see cref="LoginWithProviderStatus.BusinessOutcome"/>.
        /// </summary>
        public ProviderLoginBusinessOutcome BusinessOutcome { get; }

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
        /// The preserved SDK error. Non-null on <see cref="LoginWithProviderStatus.Failure"/>. A
        /// provider that cannot be used here carries <see cref="HiveErrorCode.Unavailable"/> rather
        /// than being a state of its own.
        /// </summary>
        public HiveError Error { get; }

        internal static LoginWithProviderOutcome Succeeded(
            LoginProvider provider, long playerId, bool isBlocked) =>
            new LoginWithProviderOutcome(
                LoginWithProviderStatus.Success, LoginWithProviderStep.None, provider, playerId,
                isBlocked, ProviderLoginBusinessOutcome.Unrecognized, string.Empty,
                string.Empty, null);

        internal static LoginWithProviderOutcome Business(
            LoginProvider provider,
            LoginWithProviderStep failedStep,
            ProviderLoginBusinessOutcome businessOutcome,
            string rawJson) =>
            new LoginWithProviderOutcome(
                LoginWithProviderStatus.BusinessOutcome, failedStep, provider, 0, false,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        internal static LoginWithProviderOutcome Unrecognized(
            LoginProvider provider,
            LoginWithProviderStep failedStep,
            string unknownOutcomeCode,
            string rawJson) =>
            new LoginWithProviderOutcome(
                LoginWithProviderStatus.BusinessOutcome, failedStep, provider, 0, false,
                ProviderLoginBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        internal static LoginWithProviderOutcome Canceled(
            LoginProvider provider, LoginWithProviderStep failedStep) =>
            new LoginWithProviderOutcome(
                LoginWithProviderStatus.UserCanceled, failedStep, provider, 0, false,
                ProviderLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        internal static LoginWithProviderOutcome Failed(
            LoginProvider provider, LoginWithProviderStep failedStep, HiveError error) =>
            new LoginWithProviderOutcome(
                LoginWithProviderStatus.Failure, failedStep, provider, 0, false,
                ProviderLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

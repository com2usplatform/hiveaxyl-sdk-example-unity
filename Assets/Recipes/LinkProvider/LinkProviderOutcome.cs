// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of a provider-link attempt.</summary>
    public enum LinkProviderStatus
    {
        /// <summary>The provider is now linked to the signed-in player.</summary>
        Success,

        /// <summary>The server answered with a non-success Outcome. See <c>BusinessOutcome</c>.</summary>
        BusinessOutcome,

        /// <summary>The user dismissed an OS or provider UI.</summary>
        UserCanceled,

        /// <summary>A technical failure. See <c>Error</c> for the preserved <see cref="HiveError"/>.</summary>
        Failure,
    }

    /// <summary>Which step of the link did not complete.</summary>
    public enum LinkProviderStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Resolving the SDK services the link needs, or finding a session to link to.</summary>
        Resolve,

        /// <summary>Getting the credential from the provider.</summary>
        AcquireCredential,

        /// <summary>Asking the server to attach the provider to this player.</summary>
        Link,
    }

    /// <summary>
    /// The link's business results, translated from the SDK Outcomes of every step into one
    /// vocabulary. Branch on these values, never on a message string.
    /// </summary>
    public enum LinkProviderBusinessOutcome
    {
        /// <summary>
        /// An Outcome this Recipe does not translate. Two cases, told apart by
        /// <see cref="LinkProviderOutcome.UnknownOutcomeCode"/>: non-empty means the server sent a
        /// code this SDK build does not know; empty means the SDK typed the Outcome but this Recipe
        /// has no value for it yet. Never guess a known value from either.
        /// </summary>
        Unrecognized = 0,

        /// <summary>
        /// <b>Conflict.</b> The provider account is already attached to a different Axyl player.
        /// Nothing is wrong with either account — the player is holding two, and only they can say
        /// which one to keep. This is the case the Recipe exists to surface: a game typically offers
        /// to continue as the other account, and losing progress is the cost of guessing.
        /// </summary>
        ProviderOwnedByOther,

        /// <summary>
        /// <b>Conflict.</b> This player already has a provider of that kind attached — a second
        /// Google account onto an account that has one. Which of the two to keep is the player's
        /// call, and the server will not choose.
        /// </summary>
        ProviderTypeAlreadyExists,

        /// <summary>
        /// <b>Conflict.</b> This exact provider account is already attached to this player, so there
        /// is nothing to do. Closer to a success than a failure from the player's side.
        /// </summary>
        ProviderAlreadyConnected,

        /// <summary>The provider is not one this app may link.</summary>
        ProviderNotSupported,

        /// <summary>The provider rejected the token the credential carried.</summary>
        ProviderTokenError,

        /// <summary>The provider's own service failed to answer.</summary>
        ProviderRequestFailed,

        /// <summary>The console has no configuration for this provider.</summary>
        ProviderConfigNotFound,

        /// <summary>The console configuration for this provider has no client info.</summary>
        ProviderClientInfoNotExists,

        /// <summary>The service has been terminated for this app.</summary>
        ServiceTerminated,

        /// <summary>The app id is unknown to the server.</summary>
        AppNotFound,

        /// <summary>The username did not pass verification.</summary>
        UsernameVerifyFailed,

        /// <summary>The username is taken.</summary>
        UsernameAlreadyExists,

        /// <summary>The username is not in a form the server accepts.</summary>
        InvalidUsernameFormat,

        /// <summary>The password is not in a form the server accepts.</summary>
        InvalidPasswordFormat,

        /// <summary>
        /// The session's app id does not match the app this call was made for. Nothing was linked.
        /// </summary>
        AppIdMismatch,

        /// <summary>
        /// The gateway context the call carried is not valid for this request. Nothing was linked.
        /// </summary>
        InvalidGatewayContext,

        /// <summary>The caller's IP is blocked. Nothing was linked.</summary>
        IpBlocked,

        /// <summary>
        /// The player named by the session is unknown to the server. Nothing was linked.
        /// </summary>
        PlayerNotFound,
    }

    /// <summary>
    /// The result of one attempt to link a provider to the signed-in player. Read
    /// <see cref="Status"/> first; on <see cref="LinkProviderStatus.BusinessOutcome"/> the value in
    /// <see cref="BusinessOutcome"/> is what the screen acts on.
    /// </summary>
    public sealed class LinkProviderOutcome
    {
        private LinkProviderOutcome(
            LinkProviderStatus status,
            LoginProvider provider,
            long playerId,
            string providerUserId,
            LinkProviderStep failedStep,
            LinkProviderBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            Provider = provider;
            PlayerId = playerId;
            ProviderUserId = providerUserId;
            FailedStep = failedStep;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public LinkProviderStatus Status { get; }

        /// <summary>The provider the attempt was for, whichever way it ended.</summary>
        public LoginProvider Provider { get; }

        /// <summary>
        /// The player the provider was attached to. Meaningful only on
        /// <see cref="LinkProviderStatus.Success"/>.
        /// </summary>
        public long PlayerId { get; }

        /// <summary>
        /// The provider account that was attached, as the server recorded it. Meaningful only on
        /// <see cref="LinkProviderStatus.Success"/>; a screen can show which account is now linked
        /// rather than only that something is.
        /// </summary>
        public string ProviderUserId { get; }

        /// <summary>
        /// The step that did not complete, or <see cref="LinkProviderStep.None"/> on success. Report
        /// it in diagnostics; do not branch game logic on it.
        /// </summary>
        public LinkProviderStep FailedStep { get; }

        /// <summary>
        /// The translated business result. Meaningful only on
        /// <see cref="LinkProviderStatus.BusinessOutcome"/>.
        /// </summary>
        public LinkProviderBusinessOutcome BusinessOutcome { get; }

        /// <summary>
        /// Whether the account state conflicts with the requested link. Read <see cref="BusinessOutcome"/>
        /// to distinguish an existing link from a conflict requiring the player's choice. Do not retry automatically.
        /// </summary>
        public bool IsConflict =>
            Status == LinkProviderStatus.BusinessOutcome
            && (BusinessOutcome == LinkProviderBusinessOutcome.ProviderOwnedByOther
                || BusinessOutcome == LinkProviderBusinessOutcome.ProviderTypeAlreadyExists
                || BusinessOutcome == LinkProviderBusinessOutcome.ProviderAlreadyConnected);

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
        /// The preserved SDK error. Non-null only on <see cref="LinkProviderStatus.Failure"/>.
        /// </summary>
        public HiveError Error { get; }

        /// <summary>The provider is linked.</summary>
        internal static LinkProviderOutcome Succeeded(
            LoginProvider provider, long playerId, string providerUserId) =>
            new LinkProviderOutcome(
                LinkProviderStatus.Success, provider, playerId, providerUserId ?? string.Empty,
                LinkProviderStep.None, LinkProviderBusinessOutcome.Unrecognized, string.Empty,
                string.Empty, null);

        /// <summary>The server answered with an Outcome this Recipe translates.</summary>
        internal static LinkProviderOutcome Business(
            LoginProvider provider,
            LinkProviderStep failedStep,
            LinkProviderBusinessOutcome businessOutcome,
            string rawJson) =>
            new LinkProviderOutcome(
                LinkProviderStatus.BusinessOutcome, provider, 0, string.Empty, failedStep,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        /// <summary>
        /// The server answered with an Outcome that is not translated — either unknown to this SDK
        /// build (<paramref name="unknownOutcomeCode"/> non-empty) or unmapped by this Recipe.
        /// </summary>
        internal static LinkProviderOutcome Unrecognized(
            LoginProvider provider,
            LinkProviderStep failedStep,
            string unknownOutcomeCode,
            string rawJson) =>
            new LinkProviderOutcome(
                LinkProviderStatus.BusinessOutcome, provider, 0, string.Empty, failedStep,
                LinkProviderBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>The user dismissed an OS or provider UI.</summary>
        internal static LinkProviderOutcome Canceled(
            LoginProvider provider, LinkProviderStep failedStep) =>
            new LinkProviderOutcome(
                LinkProviderStatus.UserCanceled, provider, 0, string.Empty, failedStep,
                LinkProviderBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        /// <summary>A technical failure; the SDK error is preserved as given.</summary>
        internal static LinkProviderOutcome Failed(
            LoginProvider provider, LinkProviderStep failedStep, HiveError error) =>
            new LinkProviderOutcome(
                LinkProviderStatus.Failure, provider, 0, string.Empty, failedStep,
                LinkProviderBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of a logout attempt.</summary>
    public enum LogoutStatus
    {
        /// <summary>The server ended the requested session. See SessionCleared for local cleanup.</summary>
        Success,

        /// <summary>The server answered with a non-success Outcome. See <c>BusinessOutcome</c>.</summary>
        BusinessOutcome,

        /// <summary>A technical failure. See <c>Error</c> for the preserved <see cref="HiveError"/>.</summary>
        Failure,
    }

    /// <summary>Which step of the logout did not complete.</summary>
    public enum LogoutStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Resolving the SDK services the logout needs, or finding a session to end.</summary>
        Resolve,

        /// <summary>Asking the server to end the session.</summary>
        Logout,
    }

    /// <summary>
    /// The logout's business results, translated from the SDK Outcome into one vocabulary. Branch on
    /// these values, never on a message string.
    /// </summary>
    public enum LogoutBusinessOutcome
    {
        /// <summary>
        /// An Outcome this Recipe does not translate. Two cases, told apart by
        /// <see cref="LogoutOutcome.UnknownOutcomeCode"/>: non-empty means the server sent a code
        /// this SDK build does not know; empty means the SDK typed the Outcome but this Recipe has
        /// no value for it yet. Never guess a known value from either.
        /// </summary>
        Unrecognized = 0,

        /// <summary>The service has been terminated for this app.</summary>
        ServiceTerminated,

        /// <summary>
        /// Guest sessions cannot be signed out through this operation. The session remains active.
        /// </summary>
        GuestSignoutBlocked,

        /// <summary>
        /// The session's app id does not match the app this call was made for. The sign-out did not happen;
        /// the session is still live.
        /// </summary>
        AppIdMismatch,

        /// <summary>
        /// The gateway context the call carried is not valid for this request. The sign-out did not happen;
        /// the session is still live.
        /// </summary>
        InvalidGatewayContext,

        /// <summary>The app id is unknown to the server. The sign-out did not happen.</summary>
        AppNotFound,

        /// <summary>
        /// The player named by the session is unknown to the server. The sign-out did not
        /// happen.
        /// </summary>
        PlayerNotFound,
    }

    /// <summary>
    /// The result of one logout attempt. Read <see cref="Status"/> first, then
    /// <see cref="SessionCleared"/> — they are separate questions, and which of them a screen acts on
    /// is not the same one.
    /// </summary>
    public sealed class LogoutOutcome
    {
        private LogoutOutcome(
            LogoutStatus status,
            LogoutStep failedStep,
            bool sessionCleared,
            LogoutBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            SessionCleared = sessionCleared;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public LogoutStatus Status { get; }

        /// <summary>
        /// The step that did not complete, or <see cref="LogoutStep.None"/> on success. Report it in
        /// diagnostics; do not branch game logic on it.
        /// </summary>
        public LogoutStep FailedStep { get; }

        /// <summary>
        /// Whether the local session was cleared. This is the question a screen acts on, and it does
        /// not follow from <see cref="Status"/>: a cancelled call clears when the server finished
        /// anyway, and a <see cref="LogoutStatus.Success"/> does not when the session was replaced
        /// while the call was in flight.
        /// </summary>
        /// <remarks>
        /// True means this operation cleared the local session. False means it did not clear it;
        /// there may have been no session, or a changed session may have been left untouched.
        /// This flag does not establish whether a current session exists or remains usable.
        /// </remarks>
        public bool SessionCleared { get; }

        /// <summary>
        /// The translated business result. Meaningful only on
        /// <see cref="LogoutStatus.BusinessOutcome"/>.
        /// </summary>
        public LogoutBusinessOutcome BusinessOutcome { get; }

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
        /// The preserved SDK error. Non-null only on <see cref="LogoutStatus.Failure"/>.
        /// </summary>
        public HiveError Error { get; }

        /// <summary>The server ended the session and the local session was cleared.</summary>
        internal static LogoutOutcome Succeeded() =>
            new LogoutOutcome(
                LogoutStatus.Success, LogoutStep.None, true,
                LogoutBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        /// <summary>
        /// The server ended the session this call started against, but a different one is live now —
        /// the app signed in again, or Core refreshed, while the call was in flight. Nothing was
        /// cleared, because the session on the device is no longer the one that was logged out.
        /// </summary>
        internal static LogoutOutcome SucceededWithSessionReplaced() =>
            new LogoutOutcome(
                LogoutStatus.Success, LogoutStep.None, false,
                LogoutBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        /// <summary>The server answered with an Outcome this Recipe translates.</summary>
        internal static LogoutOutcome Business(
            LogoutBusinessOutcome businessOutcome, string rawJson) =>
            new LogoutOutcome(
                LogoutStatus.BusinessOutcome, LogoutStep.Logout, false,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        /// <summary>
        /// The server answered with an Outcome that is not translated — either unknown to this SDK
        /// build (<paramref name="unknownOutcomeCode"/> non-empty) or unmapped by this Recipe.
        /// </summary>
        internal static LogoutOutcome Unrecognized(string unknownOutcomeCode, string rawJson) =>
            new LogoutOutcome(
                LogoutStatus.BusinessOutcome, LogoutStep.Logout, false,
                LogoutBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>
        /// A technical failure; the SDK error is preserved as given.
        /// <paramref name="sessionCleared"/> says whether the player was signed out anyway.
        /// </summary>
        internal static LogoutOutcome Failed(
            LogoutStep failedStep, HiveError error, bool sessionCleared) =>
            new LogoutOutcome(
                LogoutStatus.Failure, failedStep, sessionCleared,
                LogoutBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

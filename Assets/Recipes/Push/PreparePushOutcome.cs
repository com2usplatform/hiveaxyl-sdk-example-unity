// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of one attempt to prepare this device for push.</summary>
    public enum PreparePushStatus
    {
        /// <summary>Permission granted, token issued, and the server holds the registration.</summary>
        Success,

        /// <summary>
        /// Notification permission was not granted. Nothing was registered by this call.
        /// This status does not indicate whether another request can show a prompt.
        /// </summary>
        PermissionDenied,

        /// <summary>The server answered the registration with a non-success Outcome.</summary>
        BusinessOutcome,

        /// <summary>A technical failure. See <c>Error</c>.</summary>
        Failure,
    }

    /// <summary>Which step of the preparation did not complete.</summary>
    public enum PreparePushStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Checking what the caller supplied, before any call is made.</summary>
        Validate,

        /// <summary>Resolving the SDK services the preparation needs.</summary>
        Resolve,

        /// <summary>Asking the OS for notification permission.</summary>
        Authorize,

        /// <summary>Having the platform issue the device token.</summary>
        Token,

        /// <summary>Registering the token at the server.</summary>
        Register,
    }

    /// <summary>
    /// The business results the token registry reports. Branch on these values, never on a message
    /// string. New values may be added in a minor release — branch with a default arm rather than
    /// exhaustively.
    /// </summary>
    public enum PushBusinessOutcome
    {
        /// <summary>
        /// An Outcome this Recipe does not translate. <c>UnknownOutcomeCode</c> carries the server's
        /// own code when it sent one. Never guess a known value from it.
        /// </summary>
        Unrecognized = 0,

        /// <summary>The request targets something outside the authenticated subject's scope.</summary>
        PushResourceNotInScope,

        /// <summary>The call was made without a subject the registry accepts.</summary>
        PushInvalidSubject,
    }

    /// <summary>
    /// The result of preparing this device for push. Read <see cref="Status"/> first; the other
    /// members are meaningful per the state documented on each.
    /// </summary>
    public sealed class PreparePushOutcome
    {
        private PreparePushOutcome(
            PreparePushStatus status,
            PreparePushStep failedStep,
            string deviceToken,
            PushTokenProvider provider,
            PushBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            DeviceToken = deviceToken ?? string.Empty;
            Provider = provider;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public PreparePushStatus Status { get; }

        /// <summary>Which step did not complete, or <c>None</c> on success.</summary>
        public PreparePushStep FailedStep { get; }

        /// <summary>
        /// The registered device token, on <see cref="PreparePushStatus.Success"/> alone. Sensitive
        /// enough to keep out of logs — show it masked.
        /// </summary>
        public string DeviceToken { get; }

        /// <summary>Who the token belongs to. Meaningful when <see cref="DeviceToken"/> is.</summary>
        public PushTokenProvider Provider { get; }

        /// <summary>The translated business result, on <see cref="PreparePushStatus.BusinessOutcome"/>.</summary>
        public PushBusinessOutcome BusinessOutcome { get; }

        /// <summary>The server's unrecognized Outcome code, or empty. Diagnostics only.</summary>
        public string UnknownOutcomeCode { get; }

        /// <summary>The unparsed response body. Diagnostics only; never branch on it.</summary>
        public string RawJson { get; }

        /// <summary>The preserved error, on <see cref="PreparePushStatus.Failure"/> alone.</summary>
        public HiveError Error { get; }

        internal static PreparePushOutcome Succeeded(string deviceToken, PushTokenProvider provider) =>
            new PreparePushOutcome(
                PreparePushStatus.Success, PreparePushStep.None, deviceToken, provider,
                PushBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        internal static PreparePushOutcome DeniedByPlayer() =>
            new PreparePushOutcome(
                PreparePushStatus.PermissionDenied, PreparePushStep.Authorize, string.Empty, default,
                PushBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        internal static PreparePushOutcome Business(
            PreparePushStep failedStep, PushBusinessOutcome businessOutcome, string rawJson) =>
            new PreparePushOutcome(
                PreparePushStatus.BusinessOutcome, failedStep, string.Empty, default,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        internal static PreparePushOutcome Unrecognized(
            PreparePushStep failedStep, string unknownOutcomeCode, string rawJson) =>
            new PreparePushOutcome(
                PreparePushStatus.BusinessOutcome, failedStep, string.Empty, default,
                PushBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        internal static PreparePushOutcome Failed(PreparePushStep failedStep, HiveError error) =>
            new PreparePushOutcome(
                PreparePushStatus.Failure, failedStep, string.Empty, default,
                PushBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

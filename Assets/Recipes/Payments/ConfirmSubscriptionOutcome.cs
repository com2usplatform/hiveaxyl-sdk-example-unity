// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of one attempt to confirm a subscription.</summary>
    public enum ConfirmSubscriptionStatus
    {
        /// <summary>Confirmed at the server, and closed at the store where one has a close.</summary>
        Success,

        /// <summary>The server answered with a non-success Outcome.</summary>
        BusinessOutcome,

        /// <summary>A technical failure. See <c>Error</c>.</summary>
        Failure,
    }

    /// <summary>Which step of the confirm did not complete.</summary>
    public enum ConfirmSubscriptionStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Checking what the caller supplied, before any call is made.</summary>
        Validate,

        /// <summary>Resolving the SDK services the confirm needs.</summary>
        Resolve,

        /// <summary>
        /// Confirming the subscription and closing the store transaction.
        /// </summary>
        Confirm,
    }

    /// <summary>
    /// The result of confirming a subscription. A failure here leaves a subscription that is saved,
    /// verified and granted but unconfirmed — and for Google unacknowledged, which Play refunds on
    /// its own within days — so a failed confirm is the one outcome that must be retried promptly.
    /// </summary>
    public sealed class ConfirmSubscriptionOutcome
    {
        private ConfirmSubscriptionOutcome(
            ConfirmSubscriptionStatus status,
            ConfirmSubscriptionStep failedStep,
            PendingPurchase pending,
            string transactionId,
            PurchaseBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            Pending = pending;
            TransactionId = transactionId;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public ConfirmSubscriptionStatus Status { get; }

        /// <summary>Which step did not complete, or <c>None</c> on success.</summary>
        public ConfirmSubscriptionStep FailedStep { get; }

        /// <summary>
        /// The subscription supplied to this attempt.
        /// May be null when input validation fails because no pending subscription was supplied.
        /// </summary>
        public PendingPurchase Pending { get; }

        /// <summary>The confirmed record's transaction id, when the server reported one.</summary>
        public string TransactionId { get; }

        /// <summary>
        /// The translated business result, on <see cref="ConfirmSubscriptionStatus.BusinessOutcome"/>.
        /// </summary>
        public PurchaseBusinessOutcome BusinessOutcome { get; }

        /// <summary>The server's unrecognized Outcome code, or empty. Diagnostics only.</summary>
        public string UnknownOutcomeCode { get; }

        /// <summary>The unparsed response body. Diagnostics only; never branch on it.</summary>
        public string RawJson { get; }

        /// <summary>The preserved error, on <see cref="ConfirmSubscriptionStatus.Failure"/>.</summary>
        public HiveError Error { get; }

        internal static ConfirmSubscriptionOutcome Succeeded(
            PendingPurchase pending, string transactionId) =>
            new ConfirmSubscriptionOutcome(
                ConfirmSubscriptionStatus.Success, ConfirmSubscriptionStep.None, pending,
                transactionId ?? string.Empty,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        internal static ConfirmSubscriptionOutcome Business(
            ConfirmSubscriptionStep failedStep,
            PurchaseBusinessOutcome businessOutcome,
            string rawJson,
            PendingPurchase pending) =>
            new ConfirmSubscriptionOutcome(
                ConfirmSubscriptionStatus.BusinessOutcome, failedStep, pending, string.Empty,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        internal static ConfirmSubscriptionOutcome Unrecognized(
            ConfirmSubscriptionStep failedStep,
            string unknownOutcomeCode,
            string rawJson,
            PendingPurchase pending) =>
            new ConfirmSubscriptionOutcome(
                ConfirmSubscriptionStatus.BusinessOutcome, failedStep, pending, string.Empty,
                PurchaseBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        internal static ConfirmSubscriptionOutcome Failed(
            ConfirmSubscriptionStep failedStep, HiveError error, PendingPurchase pending) =>
            new ConfirmSubscriptionOutcome(
                ConfirmSubscriptionStatus.Failure, failedStep, pending, string.Empty,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

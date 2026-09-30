// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of one attempt to start a subscription.</summary>
    public enum StartSubscriptionStatus
    {
        /// <summary>The store took the payment and the receipt is in hand.</summary>
        Success,

        /// <summary>The player closed the store's own UI without subscribing.</summary>
        UserCanceled,

        /// <summary>The server or the store answered with a non-success Outcome.</summary>
        BusinessOutcome,

        /// <summary>A technical failure. See <c>Error</c>.</summary>
        Failure,
    }

    /// <summary>Which step of the start did not complete.</summary>
    public enum StartSubscriptionStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Checking the order the caller supplied, before any call is made.</summary>
        Validate,

        /// <summary>Resolving the SDK services the subscription needs.</summary>
        Resolve,

        /// <summary>
        /// Recording subscription purchase intent.
        /// </summary>
        Prepare,

        /// <summary>The store taking the payment.</summary>
        StorePurchase,
    }

    /// <summary>
    /// The result of starting a subscription. Read <see cref="Status"/> first; the other members are
    /// meaningful per the state documented on each.
    /// </summary>
    public sealed class StartSubscriptionOutcome
    {
        private StartSubscriptionOutcome(
            StartSubscriptionStatus status,
            StartSubscriptionStep failedStep,
            PendingPurchase pending,
            PurchaseBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            Pending = pending;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public StartSubscriptionStatus Status { get; }

        /// <summary>Which step did not complete, or <c>None</c> on success.</summary>
        public StartSubscriptionStep FailedStep { get; }

        /// <summary>
        /// The subscription to carry through the rest of the flow. Non-null on success — and by then
        /// the player has paid, so losing it loses the purchase's way forward until the store
        /// redelivers the transaction.
        /// </summary>
        public PendingPurchase Pending { get; }

        /// <summary>The translated business result, on <see cref="StartSubscriptionStatus.BusinessOutcome"/>.</summary>
        public PurchaseBusinessOutcome BusinessOutcome { get; }

        /// <summary>The server's unrecognized Outcome code, or empty. Diagnostics only.</summary>
        public string UnknownOutcomeCode { get; }

        /// <summary>The unparsed response body. Diagnostics only; never branch on it.</summary>
        public string RawJson { get; }

        /// <summary>The preserved error, on <see cref="StartSubscriptionStatus.Failure"/>.</summary>
        public HiveError Error { get; }

        internal static StartSubscriptionOutcome Succeeded(PendingPurchase pending) =>
            new StartSubscriptionOutcome(
                StartSubscriptionStatus.Success, StartSubscriptionStep.None, pending,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        internal static StartSubscriptionOutcome Canceled() =>
            new StartSubscriptionOutcome(
                StartSubscriptionStatus.UserCanceled, StartSubscriptionStep.StorePurchase, null,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        internal static StartSubscriptionOutcome Business(
            StartSubscriptionStep failedStep, PurchaseBusinessOutcome businessOutcome, string rawJson) =>
            new StartSubscriptionOutcome(
                StartSubscriptionStatus.BusinessOutcome, failedStep, null,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        internal static StartSubscriptionOutcome Unrecognized(
            StartSubscriptionStep failedStep, string unknownOutcomeCode, string rawJson) =>
            new StartSubscriptionOutcome(
                StartSubscriptionStatus.BusinessOutcome, failedStep, null,
                PurchaseBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        internal static StartSubscriptionOutcome Failed(
            StartSubscriptionStep failedStep, HiveError error) =>
            new StartSubscriptionOutcome(
                StartSubscriptionStatus.Failure, failedStep, null,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

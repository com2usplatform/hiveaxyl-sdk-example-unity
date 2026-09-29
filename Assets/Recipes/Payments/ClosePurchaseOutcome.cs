// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of one attempt to close a verified purchase.</summary>
    public enum ClosePurchaseStatus
    {
        /// <summary>The transaction is closed at the market or the server.</summary>
        Success,

        /// <summary>The server answered with a non-success Outcome. See <c>BusinessOutcome</c>.</summary>
        BusinessOutcome,

        /// <summary>A technical failure. See <c>Error</c> for the preserved <see cref="HiveError"/>.</summary>
        Failure,
    }

    /// <summary>Which step of the close did not complete.</summary>
    public enum ClosePurchaseStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Checking what the caller supplied, before any call is made.</summary>
        Validate,

        /// <summary>Resolving the SDK services the close needs.</summary>
        Resolve,

        /// <summary>Closing the transaction at the market or the server.</summary>
        Finalize,
    }

    /// <summary>
    /// The result of closing a purchase. Read <see cref="Status"/> first; the other members are
    /// meaningful per the state documented on each.
    /// </summary>
    /// <remarks>
    /// When closing fails after verification and delivery, preserve any returned <see cref="Pending"/>
    /// to retry closing without granting the item again.
    /// Input validation failures may return no purchase; see <see cref="Pending"/>.
    /// </remarks>
    public sealed class ClosePurchaseOutcome
    {
        private ClosePurchaseOutcome(
            ClosePurchaseStatus status,
            ClosePurchaseStep failedStep,
            PendingPurchase pending,
            string orderId,
            string storeTransactionId,
            PurchaseBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            Pending = pending;
            OrderId = orderId;
            StoreTransactionId = storeTransactionId;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public ClosePurchaseStatus Status { get; }

        /// <summary>Which step did not complete, or <c>None</c> on success.</summary>
        public ClosePurchaseStep FailedStep { get; }

        /// <summary>
        /// The purchase supplied to this attempt.
        /// May be null when input validation fails because no purchase was supplied.
        /// </summary>
        public PendingPurchase Pending { get; }

        /// <summary>The order id the close reported, when it reported one.</summary>
        public string OrderId { get; }

        /// <summary>The store's own transaction id the close reported, when it reported one.</summary>
        public string StoreTransactionId { get; }

        /// <summary>
        /// The translated business result. Meaningful only on
        /// <see cref="ClosePurchaseStatus.BusinessOutcome"/>.
        /// </summary>
        public PurchaseBusinessOutcome BusinessOutcome { get; }

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
        /// The preserved SDK error. Non-null only on <see cref="ClosePurchaseStatus.Failure"/>.
        /// </summary>
        public HiveError Error { get; }

        /// <summary>The purchase is closed.</summary>
        internal static ClosePurchaseOutcome Succeeded(
            PendingPurchase pending, string orderId, string storeTransactionId) =>
            new ClosePurchaseOutcome(
                ClosePurchaseStatus.Success, ClosePurchaseStep.None, pending,
                orderId ?? string.Empty, storeTransactionId ?? string.Empty,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        /// <summary>The server answered with an Outcome this Recipe translates.</summary>
        internal static ClosePurchaseOutcome Business(
            ClosePurchaseStep failedStep,
            PurchaseBusinessOutcome businessOutcome,
            string rawJson,
            PendingPurchase pending) =>
            new ClosePurchaseOutcome(
                ClosePurchaseStatus.BusinessOutcome, failedStep, pending, string.Empty, string.Empty,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        /// <summary>
        /// The server answered with an Outcome that is not translated — either unknown to this SDK
        /// build (<paramref name="unknownOutcomeCode"/> non-empty) or unmapped by this Recipe.
        /// </summary>
        internal static ClosePurchaseOutcome Unrecognized(
            ClosePurchaseStep failedStep,
            string unknownOutcomeCode,
            string rawJson,
            PendingPurchase pending) =>
            new ClosePurchaseOutcome(
                ClosePurchaseStatus.BusinessOutcome, failedStep, pending, string.Empty, string.Empty,
                PurchaseBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>A technical failure; the SDK error is preserved as given.</summary>
        internal static ClosePurchaseOutcome Failed(
            ClosePurchaseStep failedStep, HiveError error, PendingPurchase pending) =>
            new ClosePurchaseOutcome(
                ClosePurchaseStatus.Failure, failedStep, pending, string.Empty, string.Empty,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of one attempt to start a purchase.</summary>
    public enum InitiatePurchaseStatus
    {
        /// <summary>The store has an order and the app holds a receipt.</summary>
        Success,

        /// <summary>The server answered with a non-success Outcome. See <c>BusinessOutcome</c>.</summary>
        BusinessOutcome,

        /// <summary>The player closed the market's own UI without buying. Not an error to report.</summary>
        UserCanceled,

        /// <summary>
        /// A PG payment page is ready. The app opens <c>ExternalUrl</c> and, when the player returns,
        /// calls <c>PreparePurchaseAsync</c> with <c>Pending</c> to recover the receipt and record the purchase.
        /// Returning from the page does not prove payment. The app then requests game-server verification
        /// and delivery. After delivery is confirmed, it calls <c>ClosePurchaseAsync</c>.
        /// </summary>
        AwaitingExternal,

        /// <summary>A technical failure. See <c>Error</c> for the preserved <see cref="HiveError"/>.</summary>
        Failure,
    }

    /// <summary>Which step of the attempt did not complete.</summary>
    public enum InitiatePurchaseStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Checking the order the caller supplied, before any call is made.</summary>
        Validate,

        /// <summary>Resolving the SDK services the purchase needs.</summary>
        Resolve,

        /// <summary>
        /// Recording the intent to buy, before any store UI opens. Failing here means no store was
        /// opened and no money moved.
        /// </summary>
        PrePurchase,

        /// <summary>Taking the payment at the market.</summary>
        StorePurchase,
    }

    /// <summary>
    /// The business results the payment endpoints report. Branch on these values, never on a message
    /// string.
    /// </summary>
    /// <remarks>
    /// Handle unrecognized values with a default branch.
    /// Store conditions and <see cref="NothingToRestore"/> are not server codes.
    /// </remarks>
    public enum PurchaseBusinessOutcome
    {
        /// <summary>
        /// An Outcome this Recipe does not translate. Two cases, told apart by
        /// <c>UnknownOutcomeCode</c>: non-empty means the server sent a code this SDK build does not
        /// know; empty means the SDK typed the Outcome but this Recipe has no value for it yet. Never
        /// guess a known value from either.
        /// </summary>
        Unrecognized = 0,

        /// <summary>The request was malformed. The server's message names what it objected to.</summary>
        PaymentBadRequest,

        /// <summary>A request field was rejected as invalid.</summary>
        PaymentInvalidParameter,

        /// <summary>The order, product or receipt the call named does not exist.</summary>
        PaymentResourceNotFound,

        /// <summary>
        /// The record the call would create already exists — a subscription saved by an earlier run.
        /// A rerun's shape, not a fault: the verify decides what the receipt is worth.
        /// </summary>
        PaymentResourceConflict,

        /// <summary>The call was made without a usable session.</summary>
        PaymentUnauthorized,

        /// <summary>
        /// The market refused the receipt. The money may still have moved, so the receipt is kept and the
        /// server's message says what the market objected to.
        /// </summary>
        VerifyError,

        /// <summary>
        /// The market has not taken the money yet — a Play purchase awaiting a cash top-up, or an Apple
        /// Ask to Buy awaiting a parent. Nothing is owed, so nothing may be granted; the market delivers
        /// the purchase later if it completes.
        /// </summary>
        /// <remarks>
        /// A market condition and not a server code, which is why it is named for the store rather than
        /// with the <c>Payment</c> prefix the endpoints use. No call was made when this is reported.
        /// </remarks>
        StorePurchasePending,

        /// <summary>
        /// The market says the player already owns the product, which for a consumable means an earlier
        /// purchase was never consumed. Buying again cannot fix it — the open one has to be closed first.
        /// </summary>
        /// <remarks>A market condition and not a server code. No call was made when this is reported.</remarks>
        StoreItemAlreadyOwned,

        /// <summary>
        /// The server holds no unconsumed order to close. For a market whose receipt is recovered rather
        /// than handed over — PG — this is what "the player has not finished paying yet" looks like, and
        /// it is also what an order already consumed looks like.
        /// </summary>
        /// <remarks>
        /// No matching open purchase was found. Retry after external payment is complete.
        /// </remarks>
        NothingToRestore,
    }

    /// <summary>
    /// The result of starting a purchase. Read <see cref="Status"/> first; the other members are
    /// meaningful per the state documented on each.
    /// </summary>
    public sealed class InitiatePurchaseOutcome
    {
        private InitiatePurchaseOutcome(
            InitiatePurchaseStatus status,
            InitiatePurchaseStep failedStep,
            PendingPurchase pending,
            string externalUrl,
            PurchaseBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            Pending = pending;
            ExternalUrl = externalUrl ?? string.Empty;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public InitiatePurchaseStatus Status { get; }

        /// <summary>
        /// The step that did not complete, or <see cref="InitiatePurchaseStep.None"/> on success.
        /// Report it in diagnostics; do not branch game logic on it.
        /// </summary>
        public InitiatePurchaseStep FailedStep { get; }

        /// <summary>
        /// The purchase to pass to <see cref="ConsumablePurchaseRecipe.PreparePurchaseAsync"/>.
        /// Non-null on <see cref="InitiatePurchaseStatus.Success"/> and
        /// <see cref="InitiatePurchaseStatus.AwaitingExternal"/>.
        /// For PG, it contains the order without a receipt until preparation recovers one.
        /// </summary>
        public PendingPurchase Pending { get; }

        /// <summary>
        /// The store's own transaction id, when the server returned one. Read from
        /// <see cref="Pending"/> rather than stored a second time, so the value a caller reports and the
        /// value the closing calls send cannot disagree.
        /// </summary>
        public string StoreTransactionId => Pending?.StoreTransactionId ?? string.Empty;

        /// <summary>
        /// Where to send the player. Non-empty only on
        /// <see cref="InitiatePurchaseStatus.AwaitingExternal"/>.
        /// </summary>
        public string ExternalUrl { get; }

        /// <summary>
        /// The translated business result. Meaningful only on
        /// <see cref="InitiatePurchaseStatus.BusinessOutcome"/>.
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
        /// The preserved SDK error. Non-null only on <see cref="InitiatePurchaseStatus.Failure"/>.
        /// </summary>
        public HiveError Error { get; }

        /// <summary>The order exists and the receipt is in hand.</summary>
        internal static InitiatePurchaseOutcome Succeeded(PendingPurchase pending) =>
            new InitiatePurchaseOutcome(
                InitiatePurchaseStatus.Success, InitiatePurchaseStep.None, pending,
                string.Empty, PurchaseBusinessOutcome.Unrecognized,
                string.Empty, string.Empty, null);

        /// <summary>The server answered with an Outcome this Recipe translates.</summary>
        internal static InitiatePurchaseOutcome Business(
            InitiatePurchaseStep failedStep, PurchaseBusinessOutcome businessOutcome, string rawJson) =>
            new InitiatePurchaseOutcome(
                InitiatePurchaseStatus.BusinessOutcome, failedStep, null, string.Empty,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        /// <summary>
        /// The server answered with an Outcome that is not translated — either unknown to this SDK
        /// build (<paramref name="unknownOutcomeCode"/> non-empty) or unmapped by this Recipe.
        /// </summary>
        internal static InitiatePurchaseOutcome Unrecognized(
            InitiatePurchaseStep failedStep, string unknownOutcomeCode, string rawJson) =>
            new InitiatePurchaseOutcome(
                InitiatePurchaseStatus.BusinessOutcome, failedStep, null, string.Empty,
                PurchaseBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>The player closed the market's UI.</summary>
        internal static InitiatePurchaseOutcome Canceled() =>
            new InitiatePurchaseOutcome(
                InitiatePurchaseStatus.UserCanceled, InitiatePurchaseStep.StorePurchase, null,
                string.Empty, PurchaseBusinessOutcome.Unrecognized, string.Empty,
                string.Empty, null);

        /// <summary>The player has to finish somewhere the app cannot wait on.</summary>
        /// <remarks>
        /// Pass <see cref="Pending"/> to <see cref="ConsumablePurchaseRecipe.PreparePurchaseAsync"/>
        /// after external payment to recover the receipt. Initiation alone does not prove payment.
        /// </remarks>
        internal static InitiatePurchaseOutcome AwaitingExternal(string url, PendingPurchase pending) =>
            new InitiatePurchaseOutcome(
                InitiatePurchaseStatus.AwaitingExternal, InitiatePurchaseStep.None, pending,
                url, PurchaseBusinessOutcome.Unrecognized, string.Empty,
                string.Empty, null);

        /// <summary>A technical failure; the SDK error is preserved as given.</summary>
        internal static InitiatePurchaseOutcome Failed(
            InitiatePurchaseStep failedStep, HiveError error) =>
            new InitiatePurchaseOutcome(
                InitiatePurchaseStatus.Failure, failedStep, null, string.Empty,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

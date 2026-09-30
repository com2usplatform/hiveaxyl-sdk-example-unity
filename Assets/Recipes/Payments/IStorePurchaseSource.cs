// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Payments;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>Which market a purchase is being made in.</summary>
    public enum PurchaseMarket
    {
        /// <summary>Google Play.</summary>
        Google,

        /// <summary>The Apple App Store.</summary>
        Apple,

        /// <summary>Steam.</summary>
        Steam,

        /// <summary>A PG / web payment page opened in a browser.</summary>
        Pg,
    }

    /// <summary>How a market's own part of a purchase ended.</summary>
    public enum StorePurchaseStatus
    {
        /// <summary>The market took the payment and the receipt is in hand.</summary>
        ReceiptReady,

        /// <summary>
        /// The market handed back somewhere for the player to go, and cannot say when they come back.
        /// Only PG answers this way: the payment page is a browser the app opens, and the result has to
        /// be asked for afterwards rather than waited on.
        /// </summary>
        AwaitingExternal,

        /// <summary>The player closed the market's own UI without buying.</summary>
        UserCanceled,

        /// <summary>The market or the server answered with a non-success Outcome.</summary>
        BusinessOutcome,

        /// <summary>A technical failure. See <c>Error</c>.</summary>
        Failure,
    }

    /// <summary>The result of a market's own part of a purchase.</summary>
    public sealed class StorePurchaseResult
    {
        private StorePurchaseResult(
            StorePurchaseStatus status,
            string axylReceipt,
            string verifyToken,
            string finishToken,
            string orderId,
            string storeTransactionId,
            string externalUrl,
            PurchaseBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error,
            string storeVerificationError = null)
        {
            Status = status;
            AxylReceipt = axylReceipt;
            VerifyToken = verifyToken;
            FinishToken = finishToken;
            OrderId = orderId;
            StoreTransactionId = storeTransactionId;
            ExternalUrl = externalUrl;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
            StoreVerificationError = storeVerificationError ?? string.Empty;
        }

        /// <summary>How it ended.</summary>
        public StorePurchaseStatus Status { get; }

        /// <summary>
        /// The market receipt to preserve for game-server verification.
        /// </summary>
        public string AxylReceipt { get; }

        /// <summary>
        /// The market-specific verification token; it may differ from the receipt.
        /// </summary>
        public string VerifyToken { get; }

        /// <summary>
        /// What closing the transaction at the market needs — Google's and Apple's own finish calls take
        /// a token of their own. Empty for the markets that close through the server.
        /// </summary>
        public string FinishToken { get; }

        /// <summary>The order id, when the market or server assigned one.</summary>
        public string OrderId { get; }

        /// <summary>The market's own transaction id, when it gave one.</summary>
        public string StoreTransactionId { get; }

        /// <summary>
        /// Where to send the player. Non-empty only on
        /// <see cref="StorePurchaseStatus.AwaitingExternal"/>.
        /// </summary>
        public string ExternalUrl { get; }

        /// <summary>The translated business result, on <see cref="StorePurchaseStatus.BusinessOutcome"/>.</summary>
        public PurchaseBusinessOutcome BusinessOutcome { get; }

        /// <summary>The server's unrecognized Outcome code, or empty.</summary>
        public string UnknownOutcomeCode { get; }

        /// <summary>The unparsed response body. Diagnostics only.</summary>
        public string RawJson { get; }

        /// <summary>The preserved error, on <see cref="StorePurchaseStatus.Failure"/>.</summary>
        public HiveError Error { get; }

        /// <summary>
        /// What the market's own check on the receipt objected to, or empty when it did not object —
        /// which is also what a market with no check of its own reports.
        /// </summary>
        /// <remarks>
        /// Only Apple has a verdict that speaks separately from the receipt: StoreKit hands back a
        /// transaction marked verified or unverified, and the addon keeps both branches rather than
        /// flattening them to a bool. It is carried rather than acted on, because the server's verify is
        /// the authority and what to do about an unverified transaction is the game's policy — but a
        /// source that dropped it would take that decision away from the game.
        /// </remarks>
        public string StoreVerificationError { get; }

        /// <summary>The market took the payment.</summary>
        public static StorePurchaseResult Ready(
            string axylReceipt,
            string verifyToken = null,
            string finishToken = null,
            string orderId = null,
            string storeTransactionId = null,
            string storeVerificationError = null) =>
            new StorePurchaseResult(
                StorePurchaseStatus.ReceiptReady, axylReceipt, verifyToken ?? axylReceipt,
                finishToken ?? string.Empty, orderId, storeTransactionId, string.Empty,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, null,
                storeVerificationError);

        /// <summary>The player has to go somewhere, and the app has to ask for the result later.</summary>
        public static StorePurchaseResult AwaitingExternal(string url, string orderId = null) =>
            new StorePurchaseResult(
                StorePurchaseStatus.AwaitingExternal, string.Empty, string.Empty, string.Empty,
                orderId, null, url, PurchaseBusinessOutcome.Unrecognized, string.Empty,
                string.Empty, null);

        /// <summary>The player closed the market's UI.</summary>
        public static StorePurchaseResult Canceled() =>
            new StorePurchaseResult(
                StorePurchaseStatus.UserCanceled, string.Empty, string.Empty, string.Empty, null,
                null, string.Empty, PurchaseBusinessOutcome.Unrecognized, string.Empty,
                string.Empty, null);

        /// <summary>A translated non-success Outcome.</summary>
        public static StorePurchaseResult Business(
            PurchaseBusinessOutcome businessOutcome, string rawJson = null) =>
            new StorePurchaseResult(
                StorePurchaseStatus.BusinessOutcome, string.Empty, string.Empty, string.Empty, null,
                null, string.Empty, businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        /// <summary>An Outcome nothing translated.</summary>
        public static StorePurchaseResult Unrecognized(string unknownOutcomeCode, string rawJson = null) =>
            new StorePurchaseResult(
                StorePurchaseStatus.BusinessOutcome, string.Empty, string.Empty, string.Empty, null,
                null, string.Empty, PurchaseBusinessOutcome.Unrecognized,
                unknownOutcomeCode ?? string.Empty, rawJson ?? string.Empty, null);

        /// <summary>A technical failure.</summary>
        public static StorePurchaseResult Failed(HiveError error) =>
            new StorePurchaseResult(
                StorePurchaseStatus.Failure, string.Empty, string.Empty, string.Empty, null, null,
                string.Empty, PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }

    /// <summary>
    /// One market's half of a consumable purchase: taking the payment, and closing the transaction at
    /// the market once the item has been granted.
    /// </summary>
    /// <remarks>
    /// Adapters initiate store purchases and close them after game-server verification and delivery.
    /// Receipt, verification token, and finish token may differ; preserve each returned value.
    /// Implementations do not log credentials or automatically retry.
    /// </remarks>
    public interface IStorePurchaseSource
    {
        /// <summary>Which market this is, which is also what the server calls it.</summary>
        PurchaseMarket Market { get; }

        /// <summary>
        /// Whether the purchase intent is recorded at the server before the store is reached.
        /// </summary>
        bool RecordsIntentFirst { get; }

        /// <summary>
        /// Whether the receipt is recovered from the server rather than handed over by the market.
        /// </summary>
        /// <remarks>
        /// For PG, initiation returns no receipt. Preparation recovers it after external payment.
        /// </remarks>
        bool RecoversPendingFromServer { get; }

        /// <summary>
        /// Takes the payment for <paramref name="order"/>. Runs after the pre-purchase record and
        /// before anything else.
        /// </summary>
        Task<StorePurchaseResult> PurchaseAsync(PurchaseOrder order, CancellationToken cancellationToken);

        /// <summary>
        /// Returns open store purchases, or null when recovery must query the server instead.
        /// </summary>
        Task<StoreOpenPurchasesResult> FindOpenPurchasesAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Closes the transaction at the market, after the game has granted the item. Markets that
        /// close through the server answer <c>null</c>, and the Recipe calls
        /// <c>FinalizePurchaseAsync</c> instead.
        /// </summary>
        /// <remarks>
        /// Store-side closing may also require server confirmation before finishing the transaction.
        /// </remarks>
        /// <param name="pending">The purchase being closed.</param>
        /// <param name="finalizeReceipt">
        /// The closing receipt returned by the game server, for markets that close server-side.
        /// </param>
        /// <param name="cancellationToken">Cancels the call.</param>
        Task<IAxylResult> FinishAsync(
            PendingPurchase pending, string finalizeReceipt, CancellationToken cancellationToken);
    }
}

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Payments;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Stores order, receipt, and market-specific data used by purchase and subscription Recipes.
    /// </summary>
    /// <remarks>
    /// Preserve any returned purchase data when processing stops.
    /// This object alone does not prove payment, verification, or delivery.
    /// PG initiation can return purchase data before payment, with no receipt yet.
    /// </remarks>
    public sealed class PendingPurchase
    {
        /// <summary>Creates a pending purchase.</summary>
        /// <param name="axylReceipt">
        /// The purchase receipt. May be empty during PG initiation, before receipt recovery.
        /// </param>
        /// <param name="orderId">The order the server assigned, when it assigned one.</param>
        /// <param name="order">The order this purchase was started from. Must not be null.</param>
        /// <param name="market">Which market took the payment.</param>
        /// <param name="verifyToken">
        /// The market-specific verification token. Falls back to the receipt when null.
        /// </param>
        /// <param name="finishToken">
        /// What the market's own close call takes, for the markets that have one. Empty otherwise.
        /// </param>
        /// <param name="storeTransactionId">
        /// The market's own transaction number, when it gave one. The closing calls send it as their
        /// lookup and deduplication key, so it travels with the purchase rather than being reported once
        /// and then lost.
        /// </param>
        public PendingPurchase(
            string axylReceipt,
            string orderId,
            PurchaseOrder order,
            PurchaseMarket market = PurchaseMarket.Steam,
            string verifyToken = null,
            string finishToken = null,
            string storeTransactionId = null,
            string storeVerificationError = null)
        {
            AxylReceipt = axylReceipt;
            OrderId = orderId;
            Order = order;
            Market = market;
            VerifyToken = string.IsNullOrEmpty(verifyToken) ? axylReceipt : verifyToken;
            FinishToken = finishToken ?? string.Empty;
            StoreTransactionId = storeTransactionId ?? string.Empty;
            StoreVerificationError = storeVerificationError ?? string.Empty;
        }

        /// <summary>Which market took the payment. Decides how the transaction is closed.</summary>
        public PurchaseMarket Market { get; }

        /// <summary>
        /// The market-specific token supplied for game-server verification.
        /// </summary>
        public string VerifyToken { get; }

        /// <summary>What the market's own close call takes, or empty.</summary>
        public string FinishToken { get; }

        /// <summary>The purchase receipt, or empty before a PG receipt has been recovered.</summary>
        public string AxylReceipt { get; }

        /// <summary>The order id the server assigned, or null when it did not.</summary>
        public string OrderId { get; }

        /// <summary>
        /// The store transaction ID, or empty when unavailable. Preserve it for transaction correlation.
        /// </summary>
        public string StoreTransactionId { get; }

        /// <summary>
        /// What the market's own check on the receipt objected to, or empty when it did not object.
        /// </summary>
        /// <remarks>
        /// Non-empty means StoreKit handed back a transaction it could not verify. The purchase is still
        /// carried through — the server's verify is the authority and will refuse a receipt that does not
        /// hold up — but a game with a policy of its own reads this and decides before granting anything.
        /// </remarks>
        public string StoreVerificationError { get; }

        /// <summary>
        /// The order this started from. The closing calls need its price, currency, country and
        /// language, so it travels rather than being asked for twice.
        /// </summary>
        public PurchaseOrder Order { get; }

        /// <summary>
        /// Builds the request to record this purchase. Requires <see cref="Order"/>.
        /// </summary>
        internal PurchaseRequest ToRecordRequest() =>
            new PurchaseRequest
            {
                AxylReceipt = AxylReceipt,
                ProviderId = PurchaseProviders.ToPurchase(Market),
                ProductId = Order.ProductId,
                Country = Order.Country,
                Currency = Order.Currency,
                Language = Order.Language,
                Price = Order.Price,
                OrderId = OrderId,
                StoreTransactionId = StoreTransactionId,
                ServerId = Order.ServerId,
                IapPayload = Order.IapPayload,
                AccountUuid = Order.AccountUuid,
            };
    }
}

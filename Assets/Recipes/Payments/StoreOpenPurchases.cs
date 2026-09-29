// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Collections.Generic;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// One open purchase as the market's own store reports it: paid for, never finished, and carrying
    /// whatever the store hands back — which is not a price. A store's open-purchase record names the
    /// product and the tokens; what the player paid is the server's and the catalog's to say.
    /// </summary>
    public sealed class StoreOpenPurchase
    {
        /// <summary>Creates one entry as the store reported it.</summary>
        public StoreOpenPurchase(
            string productId,
            string axylReceipt,
            string verifyToken = null,
            string finishToken = null,
            string storeTransactionId = null,
            string storeVerificationError = null)
        {
            ProductId = productId;
            AxylReceipt = axylReceipt;
            VerifyToken = verifyToken;
            FinishToken = finishToken;
            StoreTransactionId = storeTransactionId;
            StoreVerificationError = storeVerificationError ?? string.Empty;
        }

        /// <summary>The product this purchase bought.</summary>
        public string ProductId { get; }

        /// <summary>
        /// The purchase receipt.
        /// </summary>
        public string AxylReceipt { get; }

        /// <summary>
        /// The verification token. Null falls back to the receipt.
        /// </summary>
        public string VerifyToken { get; }

        /// <summary>What the store's own close takes — Apple's numeric transaction id.</summary>
        public string FinishToken { get; }

        /// <summary>The store's own transaction number, when it has one.</summary>
        public string StoreTransactionId { get; }

        /// <summary>
        /// What the store's own check on the receipt objected to, or empty. Carried the same way a
        /// fresh purchase carries it — the server's verify is the authority, the game reads this.
        /// </summary>
        public string StoreVerificationError { get; }
    }

    /// <summary>
    /// What a market's own open-purchase listing answered: the entries, the failure that kept the
    /// store from answering — or an answer nobody recognizes, preserved for diagnosis.
    /// </summary>
    public sealed class StoreOpenPurchasesResult
    {
        private static readonly IReadOnlyList<StoreOpenPurchase> s_none = new StoreOpenPurchase[0];

        private StoreOpenPurchasesResult(
            IReadOnlyList<StoreOpenPurchase> purchases,
            HiveError error,
            string unknownOutcomeCode,
            string rawJson)
        {
            Purchases = purchases ?? s_none;
            Error = error;
            UnknownOutcomeCode = unknownOutcomeCode ?? string.Empty;
            RawJson = rawJson ?? string.Empty;
        }

        /// <summary>The store answered. Empty means nothing is open.</summary>
        public bool IsSuccess => Error == null && UnknownOutcomeCode.Length == 0 && RawJson.Length == 0;

        /// <summary>The open purchases the store reported. Never null; empty on failure.</summary>
        public IReadOnlyList<StoreOpenPurchase> Purchases { get; }

        /// <summary>The failure that kept the store from answering, or null.</summary>
        public HiveError Error { get; }

        /// <summary>
        /// The store's own outcome code when this SDK build did not recognize it. Empty otherwise.
        /// Diagnostics only.
        /// </summary>
        public string UnknownOutcomeCode { get; }

        /// <summary>
        /// The unparsed response, kept for forward-compatibility diagnosis only. Never branch on it.
        /// </summary>
        public string RawJson { get; }

        /// <summary>The store answered with these open purchases.</summary>
        public static StoreOpenPurchasesResult Succeeded(IReadOnlyList<StoreOpenPurchase> purchases) =>
            new StoreOpenPurchasesResult(purchases, null, string.Empty, string.Empty);

        /// <summary>The store could not answer.</summary>
        public static StoreOpenPurchasesResult Failed(HiveError error) =>
            new StoreOpenPurchasesResult(null, error, string.Empty, string.Empty);

        /// <summary>
        /// The store answered with an outcome this SDK build does not know. The code and the raw
        /// response travel so the recovery can report them instead of flattening them into a guess.
        /// </summary>
        public static StoreOpenPurchasesResult Unrecognized(string unknownOutcomeCode, string rawJson) =>
            new StoreOpenPurchasesResult(null, null, unknownOutcomeCode, rawJson);
    }
}

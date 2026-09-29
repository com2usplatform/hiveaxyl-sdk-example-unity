// Copyright (c) Com2uS Platform Corp. All rights reserved.

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>Game-server confirmation of verified, idempotent delivery for a pending purchase.</summary>
    /// <remarks>Create only after an authoritative response for the purchase being closed.
    /// This client object is an integration contract, not proof of payment. The server must check
    /// account, product and price, and current refund/expiry eligibility for subscriptions.</remarks>
    public sealed class PurchaseDeliveryConfirmation
    {
        public PurchaseDeliveryConfirmation(string receipt = null) => Receipt = receipt;

        /// <summary>The server's updated closing receipt.
        /// Empty uses the original pending receipt.</summary>
        public string Receipt { get; }
    }
}

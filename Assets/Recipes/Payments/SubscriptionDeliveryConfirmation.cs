// Copyright (c) Com2uS Platform Corp. All rights reserved.

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>Game-server confirmation of verified, idempotent delivery for a pending subscription.</summary>
    /// <remarks>Create only after an authoritative response for the subscription being confirmed.
    /// This client object is an integration contract, not proof of payment. The server must check
    /// account, product and price, and current refund/expiry eligibility.</remarks>
    public sealed class SubscriptionDeliveryConfirmation
    {
        public SubscriptionDeliveryConfirmation(string productId = null) => ProductId = productId;

        /// <summary>The server's verified product ID.
        /// Empty uses the original pending order product.</summary>
        public string ProductId { get; }
    }
}

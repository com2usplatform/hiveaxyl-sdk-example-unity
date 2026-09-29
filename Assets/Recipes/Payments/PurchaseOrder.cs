// Copyright (c) Com2uS Platform Corp. All rights reserved.

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// What the app knows about a purchase before it starts: which product, at what price, for which
    /// store account.
    /// </summary>
    /// <remarks>
    /// Supply the product, price, currency, and locale shown to the player.
    /// The game server must independently validate the purchase before delivery.
    /// </remarks>
    public sealed class PurchaseOrder
    {
        /// <summary>Creates an order.</summary>
        /// <param name="productId">The store product id the player chose. Must not be null or whitespace.</param>
        /// <param name="price">The price the player was shown.</param>
        /// <param name="currency">The currency that price is in. Must not be null or whitespace.</param>
        /// <param name="country">The store country. Must not be null or whitespace.</param>
        /// <param name="language">The store language. Must not be null or whitespace.</param>
        /// <param name="storePlayerId">The store's own account id — for Steam, the Steam ID.</param>
        /// <param name="serverId">The game server the purchase belongs to, when the game has them.</param>
        /// <param name="iapPayload">
        /// The app's own opaque payload, echoed back by the verify step so a game server can match a
        /// purchase to whatever it recorded before starting one.
        /// </param>
        /// <param name="appVersion">The app version to record with the purchase.</param>
        /// <param name="accountUuid">
        /// The account UUID the client derives from the player id, as <c>AccountUuid.Compute</c> makes it.
        /// Optional: without it the server reports the account comparison as not comparable.
        /// </param>
        public PurchaseOrder(
            string productId,
            decimal price,
            string currency,
            string country,
            string language,
            long storePlayerId,
            string serverId = null,
            string iapPayload = null,
            string appVersion = null,
            string accountUuid = null)
        {
            ProductId = productId;
            Price = price;
            Currency = currency;
            Country = country;
            Language = language;
            StorePlayerId = storePlayerId;
            ServerId = serverId;
            IapPayload = iapPayload;
            AppVersion = appVersion;
            AccountUuid = accountUuid;
        }

        /// <summary>The store product id the player chose.</summary>
        public string ProductId { get; }

        /// <summary>The price the player was shown.</summary>
        public decimal Price { get; }

        /// <summary>The currency that price is in.</summary>
        public string Currency { get; }

        /// <summary>The store country.</summary>
        public string Country { get; }

        /// <summary>The store language.</summary>
        public string Language { get; }

        /// <summary>The store's own account id — for Steam, the Steam ID.</summary>
        public long StorePlayerId { get; }

        /// <summary>The game server the purchase belongs to, or null.</summary>
        public string ServerId { get; }

        /// <summary>The app's opaque payload, or null.</summary>
        public string IapPayload { get; }

        /// <summary>The app version to record, or null.</summary>
        public string AppVersion { get; }

        /// <summary>The account UUID derived from the player id, or null.</summary>
        public string AccountUuid { get; }
    }
}

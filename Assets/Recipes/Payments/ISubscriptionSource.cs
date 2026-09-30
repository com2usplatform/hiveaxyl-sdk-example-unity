// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// One market's half of a subscription: taking the payment, and closing the store's own side of
    /// the transaction once the server has confirmed the subscription.
    /// </summary>
    /// <remarks>
    /// Supported by the Apple and Google sources.
    /// Store closing runs only after the subscription has been confirmed.
    /// </remarks>
    public interface ISubscriptionSource
    {
        /// <summary>Which market this is, which is also what the server calls it.</summary>
        PurchaseMarket Market { get; }

        /// <summary>
        /// Takes the subscription payment after the Recipe records purchase intent.
        /// </summary>
        Task<StorePurchaseResult> PurchaseAsync(PurchaseOrder order, CancellationToken cancellationToken);

        /// <summary>
        /// Closes the store transaction after confirmation. Returns null when no store close is needed.
        /// </summary>
        Task<IAxylResult> FinishAtStoreAsync(PendingPurchase pending, CancellationToken cancellationToken);
    }
}

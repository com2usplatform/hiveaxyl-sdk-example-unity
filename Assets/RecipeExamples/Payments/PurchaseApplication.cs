// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    public enum PurchaseVerificationKind
    {
        NewPurchase = 1,
        Recovery = 2,
    }

    /// <summary>
    /// Required game integration for the payment guides. These are NOT SDK interfaces.
    /// Supply real implementations; there are intentionally no successful dummy implementations.
    /// </summary>
    public interface IPurchaseApplication
    {
        // Persist receipts/order context before proceeding, even after caller cancellation.
        // Key records by account + market + order/transaction. Do not log receipt/token contents.
        Task SavePendingAsync(PendingPurchase pending);

        // Start listening BEFORE purchase initiation to avoid losing early callbacks.
        // Steam: subscribe to approval events and pump Steam callbacks; return a lease that unsubscribes.
        // PG/Apple/Google: return a no-op lease if there is no separate application-owned event.
        // This lease owns only the operation's listeners, not global SDK/store initialization.
        IDisposable ObservePayment(IStorePurchaseSource source);

        // PG: open started.ExternalUrl and wait for explicit return/resume (not proof of payment).
        // Steam: await approval matching started.Pending.OrderId, including an already-buffered event.
        // Apple/Google: the source already waited for the store; return true when ready to verify.
        // Return false on refusal/abandonment. Honor cancellation and stop waiting on disposal.
        Task<bool> WaitUntilVerifiableAsync(InitiatePurchaseOutcome started, CancellationToken token);

        // Call the game server to verify the purchase and grant exactly once.
        // Send the pending purchase data; the server independently validates the account, product, and price.
        // The verification kind is not a deduplication key.
        // Return null unless delivery is confirmed. Propagate cancellation.
        // Confirm delivery already recorded for this transaction without granting again.
        // Match the confirmation to this transaction and include any updated closing receipt.
        Task<PurchaseDeliveryConfirmation> VerifyAndGrantOnceAsync(PendingPurchase pending,
            PurchaseVerificationKind kind, CancellationToken token);

        // Also check current refund/expiry eligibility before granting the subscription entitlement.
        // A previous verification/grant alone does not establish current eligibility.
        // Return null unless delivery is confirmed; otherwise return the verified product ID.
        Task<SubscriptionDeliveryConfirmation> VerifyAndGrantSubscriptionOnceAsync(
            PendingPurchase pending, CancellationToken token);

        // Remove/mark the durable pending record only after closing succeeds.
        Task MarkClosedAsync(PendingPurchase pending);
    }
}

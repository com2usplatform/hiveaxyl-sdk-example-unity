// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>One purchase flow for every store adapter, with explicit application steps.</summary>
    public static class ConsumablePurchaseExample
    {
        // Before calling: complete SDK/store initialization and build order from the catalog price the player accepted.
        // Null later outcomes mean that stage was not attempted, never that it succeeded.
        public static async Task<(InitiatePurchaseOutcome Started, PreparePurchaseOutcome Prepared,
            ClosePurchaseOutcome Closed)> RunAsync(
            IStorePurchaseSource source, PurchaseOrder order, IPurchaseApplication app,
            CancellationToken cancellationToken = default)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }
            var recipe = new ConsumablePurchaseRecipe(source);
            using (app.ObservePayment(source))
            {
                var started = await recipe.InitiatePurchaseAsync(order, cancellationToken);
                if (started.Pending != null)
                {
                    await app.SavePendingAsync(started.Pending);
                }
                switch (started.Status)
                {
                    case InitiatePurchaseStatus.Success:
                    case InitiatePurchaseStatus.AwaitingExternal:
                        break;
                    case InitiatePurchaseStatus.UserCanceled:
                        return (started, null, null); // The player declined; nothing to grant.
                    default:
                        // Inspect BusinessOutcome or Error/FailedStep. A pending store approval
                        // is not delivery authorization. Retain any receipt for later recovery.
                        return (started, null, null);
                }

                // APP: PG opens the URL; Steam waits for matching approval. See the interface contract.
                if (!await app.WaitUntilVerifiableAsync(started, cancellationToken))
                {
                    return (started, null, null);
                }

                var prepared = await recipe.PreparePurchaseAsync(started.Pending, cancellationToken);
                if (prepared.Pending != null)
                {
                    await app.SavePendingAsync(prepared.Pending);
                }
                if (prepared.Status != PreparePurchaseStatus.Success)
                {
                    return (started, prepared, null); // Keep pending; do not grant or close.
                }

                // APP: server delivery is mandatory. Preparation alone does not authorize delivery.
                var delivery = await app.VerifyAndGrantOnceAsync(
                    prepared.Pending, PurchaseVerificationKind.NewPurchase, cancellationToken);
                if (delivery == null)
                {
                    return (started, prepared, null);
                }

                // Use prepared.Pending: PG preparation may have recovered a receipt absent at start.
                var closed = await recipe.ClosePurchaseAsync(
                    prepared.Pending, delivery, cancellationToken);
                if (closed.Status == ClosePurchaseStatus.Success)
                {
                    await app.MarkClosedAsync(prepared.Pending);
                }
                // Otherwise leave pending for recovery. Retrying close must not duplicate the grant.
                return (started, prepared, closed);
            }
        }
    }
}

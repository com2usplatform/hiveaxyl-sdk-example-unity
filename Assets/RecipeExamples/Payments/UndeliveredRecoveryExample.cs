// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>Recover purchases one at a time without granting the same order twice.</summary>
    public static class UndeliveredRecoveryExample
    {
        public static async Task<FindUndeliveredOutcome> FindAsync(
            IStorePurchaseSource source, UndeliveredQuery query, CancellationToken cancellationToken = default)
        {
            // APP: supply the signed-in account's locale/server scope. Never reuse another account's query.
            var found = await new UndeliveredPurchaseRecipe(source).FindUndeliveredAsync(query, cancellationToken);
            if (found.Status == FindUndeliveredStatus.Success)
            {
                // APP: iterate found.Undelivered (empty is normal) and await RecoverOneAsync per entry.
                // Persist ALL discovered entries before processing, so a failure cannot lose later ones.
                // Decide whether one failed entry stops the batch; do not launch duplicate recoveries.
            }
            else
            {
                // APP: inspect BusinessOutcome or Error/FailedStep. Do not treat query failure as empty.
            }
            return found;
        }

        public static async Task<(PreparePurchaseOutcome Prepared, ClosePurchaseOutcome Closed)> RecoverOneAsync(
            IStorePurchaseSource source, PendingPurchase pending, IPurchaseApplication app,
            CancellationToken cancellationToken = default)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }
            if (pending == null)
            {
                throw new ArgumentNullException(nameof(pending));
            }
            var recipe = new UndeliveredPurchaseRecipe(source);
            await app.SavePendingAsync(pending);
            // APP: where discovery lacks order metadata, reconcile it with your authoritative
            // order ledger first. Do not substitute today's price for the historical purchase price.
            var prepared = await recipe.PreparePurchaseAsync(pending, cancellationToken);
            if (prepared.Pending != null)
            {
                await app.SavePendingAsync(prepared.Pending);
            }
            if (prepared.Status != PreparePurchaseStatus.Success)
            {
                return (prepared, null);
            }

            // APP: query/grant through the SAME idempotent ledger used by new purchases.
            var delivery = await app.VerifyAndGrantOnceAsync(
                prepared.Pending, PurchaseVerificationKind.Recovery, cancellationToken);
            if (delivery == null)
            {
                return (prepared, null);
            }

            var closed = await recipe.ClosePurchaseAsync(prepared.Pending, delivery, cancellationToken);
            if (closed.Status == ClosePurchaseStatus.Success)
            {
                await app.MarkClosedAsync(prepared.Pending);
            }
            // Failed close stays recoverable even if the server already delivered the item.
            return (prepared, closed);
        }
    }
}

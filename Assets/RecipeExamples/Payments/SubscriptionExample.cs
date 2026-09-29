// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>Start a subscription with either the Apple or Google adapter.</summary>
    public static class SubscriptionExample
    {
        // Before calling: complete SDK/store initialization and build order from the selected subscription
        // product/offer.
        public static async Task<(StartSubscriptionOutcome Started, SaveSubscriptionOutcome Prepared,
            ConfirmSubscriptionOutcome Confirmed)> RunAsync(
            ISubscriptionSource source, PurchaseOrder order, IPurchaseApplication app,
            CancellationToken cancellationToken = default)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }
            var recipe = new SubscriptionRecipe(source);
            var started = await recipe.StartSubscriptionAsync(order, cancellationToken);
            if (started.Pending != null)
            {
                await app.SavePendingAsync(started.Pending);
            }
            if (started.Status != StartSubscriptionStatus.Success)
            {
                // APP: distinguish UserCanceled, BusinessOutcome (including pending approval),
                // and Failure. None authorizes entitlement delivery.
                return (started, null, null);
            }
            var prepared = await recipe.SaveSubscriptionAsync(started.Pending, cancellationToken);
            if (prepared.Pending != null)
            {
                await app.SavePendingAsync(prepared.Pending);
            }
            if (prepared.Status != SaveSubscriptionStatus.Success)
            {
                return (started, prepared, null);
            }

            // APP: the game server checks refund/expiry state and grants only an eligible entitlement, without
            // duplication.
            var delivery = await app.VerifyAndGrantSubscriptionOnceAsync(prepared.Pending, cancellationToken);
            if (delivery == null)
            {
                return (started, prepared, null);
            }

            var confirmed = await recipe.ConfirmSubscriptionAsync(
                prepared.Pending, delivery, cancellationToken);
            if (confirmed.Status == ConfirmSubscriptionStatus.Success)
            {
                await app.MarkClosedAsync(prepared.Pending);
            }
            // Keep unconfirmed state otherwise. Renewals/refunds and restart reconciliation belong
            // to your server/store integration; the consumable recovery guide is not a subscription restore.
            return (started, prepared, confirmed);
        }
    }
}

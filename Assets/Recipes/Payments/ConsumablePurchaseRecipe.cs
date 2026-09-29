// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Payments;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>Records the purchase for game-server verification.</summary>
    /// <remarks>The client prepares purchase data and closes only after the game server verifies
    /// the receipt and confirms idempotent delivery. Retain pending purchases on failure or cancellation.
    /// The game server owns verification, account/product/price checks, and entitlement eligibility.</remarks>
    public sealed class ConsumablePurchaseRecipe
    {
        private readonly IStorePurchaseSource m_source;

        /// <summary>Creates the Recipe for one market.</summary>
        /// <param name="source">
        /// The market's half of the purchase. Must not be null: a purchase with no market to make it in
        /// is not a state this Recipe could report its way out of.
        /// </param>
        public ConsumablePurchaseRecipe(IStorePurchaseSource source)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>
        /// Creates the order at the server and returns the receipt to carry through the rest of the
        /// purchase.
        /// </summary>
        /// <param name="order">What the player chose. Must not be null.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        public async Task<InitiatePurchaseOutcome> InitiatePurchaseAsync(
            PurchaseOrder order, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return InitiatePurchaseOutcome.Failed(
                    InitiatePurchaseStep.Validate,
                    new HiveError(HiveErrorCode.Cancelled, "The caller canceled before the call."));
            }

            if (order == null || string.IsNullOrWhiteSpace(order.ProductId))
            {
                return InitiatePurchaseOutcome.Failed(
                    InitiatePurchaseStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "A purchase needs a product id. Nothing was sent."));
            }

            if (!HiveCore.TryResolve<IPaymentsService>(out var payments))
            {
                return InitiatePurchaseOutcome.Failed(
                    InitiatePurchaseStep.Resolve,
                    new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "The payments Capability is not registered. Initialize the SDK with AddPayments."));
            }

            // Record purchase intent before opening the store when the source requires it.
            if (m_source.RecordsIntentFirst)
            {
                var pre = await payments.CreatePrePurchaseAsync(
                    new PrePurchase
                    {
                        ProviderId = PurchaseProviders.ToPrePurchase(m_source.Market),
                        ProductId = order.ProductId,
                        Price = order.Price,
                        Currency = order.Currency,
                        Country = order.Country,
                        Language = order.Language,
                        ServerId = order.ServerId,
                        RequestDate = DateTimeOffset.UtcNow,
                        IapPayload = order.IapPayload,
                        AccountUuid = order.AccountUuid,
                    },
                    new ApiCallContext { Token = cancellationToken });

                var preClass = SdkResultClassification.Of(pre);
                if (preClass.Kind != SdkResultKind.Success)
                {
                    return TranslateInitiate(
                        InitiatePurchaseStep.PrePurchase, preClass, PurchaseOutcomeMap.Of(pre));
                }
            }

            // The market takes it from here, and what "here" means differs: Steam has the server create
            // the order and the player approve it, Google and Apple open their own store, PG opens a
            // browser. The Recipe does not know which; the source does.
            var store = await m_source.PurchaseAsync(order, cancellationToken);
            switch (store.Status)
            {
                case StorePurchaseStatus.ReceiptReady:
                    break;

                case StorePurchaseStatus.AwaitingExternal:
                    // Preserve the order without a receipt.
                    // After external payment, PreparePurchaseAsync recovers the receipt from the server.
                    return InitiatePurchaseOutcome.AwaitingExternal(
                        store.ExternalUrl,
                        new PendingPurchase(string.Empty, store.OrderId, order, m_source.Market));

                case StorePurchaseStatus.UserCanceled:
                    return InitiatePurchaseOutcome.Canceled();

                case StorePurchaseStatus.BusinessOutcome:
                    return store.BusinessOutcome == PurchaseBusinessOutcome.Unrecognized
                        ? InitiatePurchaseOutcome.Unrecognized(
                            InitiatePurchaseStep.StorePurchase, store.UnknownOutcomeCode, store.RawJson)
                        : InitiatePurchaseOutcome.Business(
                            InitiatePurchaseStep.StorePurchase, store.BusinessOutcome, store.RawJson);

                default:
                    return InitiatePurchaseOutcome.Failed(
                        InitiatePurchaseStep.StorePurchase,
                        store.Error ?? new HiveError(
                            HiveErrorCode.Internal, "The market failed without saying why."));
            }

            // Without a receipt there is nothing to verify, finalize, or recover with later — and the
            // market may already have taken the money. Report it as a failure now rather than hand back
            // a pending purchase that cannot be closed.
            if (string.IsNullOrEmpty(store.AxylReceipt))
            {
                return InitiatePurchaseOutcome.Failed(
                    InitiatePurchaseStep.StorePurchase,
                    new HiveError(
                        HiveErrorCode.Internal,
                        "The market reported a purchase without a receipt, so it cannot be closed."));
            }

            var pending = new PendingPurchase(
                store.AxylReceipt, store.OrderId, order, m_source.Market,
                store.VerifyToken, store.FinishToken, store.StoreTransactionId,
                store.StoreVerificationError);
            return InitiatePurchaseOutcome.Succeeded(pending);
        }

        /// <summary>Records the purchase for game-server verification.
        /// Success only means preparation completed. The game server must verify and grant before closing.
        /// Preserve the returned Pending, including any recovered receipt, across retries.</summary>
        /// <param name="pending">The pending purchase; must belong to this market.</param>
        /// <param name="cancellationToken">Cancels preparation.</param>
        public async Task<PreparePurchaseOutcome> PreparePurchaseAsync(
            PendingPurchase pending, CancellationToken cancellationToken = default)
        {
            if (pending == null || pending.Order == null)
            {
                return PreparePurchaseOutcome.Failed(
                    PreparePurchaseStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "Preparing a purchase needs the pending purchase it was started with. "
                        + "Nothing was sent."),
                    pending);
            }

            // The provider every call names comes from the pending purchase — a mismatch would
            // record against the wrong market.
            if (pending.Market != m_source.Market)
            {
                return PreparePurchaseOutcome.Failed(
                    PreparePurchaseStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        $"The purchase names {pending.Market} but this Recipe purchases at "
                        + $"{m_source.Market}. Prepare it with its own market's Recipe. "
                        + "Nothing was sent."),
                    pending);
            }

            // A missing receipt means one of two things, and the source knows which: the market never
            // hands one over and the server holds it — PG, whose payment ran in a browser — or the
            // caller lost it, which no call can fix. Only the first is recovered from.
            if (string.IsNullOrWhiteSpace(pending.AxylReceipt) && !m_source.RecoversPendingFromServer)
            {
                return PreparePurchaseOutcome.Failed(
                    PreparePurchaseStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "Preparing a purchase needs the receipt it was started with. Nothing was sent."),
                    pending);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return PreparePurchaseOutcome.Failed(
                    PreparePurchaseStep.Validate,
                    new HiveError(HiveErrorCode.Cancelled, "The caller canceled before the calls."),
                    pending);
            }

            if (!HiveCore.TryResolve<IPaymentsService>(out var payments))
            {
                return PreparePurchaseOutcome.Failed(
                    PreparePurchaseStep.Resolve,
                    new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "The payments Capability is not registered. Initialize the SDK with AddPayments."),
                    pending);
            }

            var context = new ApiCallContext { Token = cancellationToken };

            if (string.IsNullOrWhiteSpace(pending.AxylReceipt))
            {
                var restored = await RestorePendingAsync(payments, pending, context);
                if (restored.Outcome != null)
                {
                    return restored.Outcome;
                }

                pending = restored.Pending;
            }

            var record = await payments.RecordStorePurchaseAsync(pending.ToRecordRequest(), context);

            var recordClass = SdkResultClassification.Of(record);
            if (recordClass.Kind != SdkResultKind.Success)
            {
                return PreparePurchaseOutcome.Translate(
                    PreparePurchaseStep.Record, recordClass, PurchaseOutcomeMap.Of(record), pending);
            }

            return PreparePurchaseOutcome.Succeeded(pending);
        }

        /// <summary>
        /// Closes a verified purchase the game server has granted: finalizes at the server for the
        /// markets that close there, or consumes / finishes at the store for the ones that close at the
        /// device.
        /// </summary>
        /// <remarks>
        /// Call only after game-server verification and idempotent delivery.
        /// If closing fails, retain the purchase and retry closing without granting again.
        /// </remarks>
        /// <param name="pending">
        /// What <see cref="PreparePurchaseAsync"/> handed back as <see cref="PreparePurchaseOutcome.Pending"/>.
        /// Must not be null. For the market whose receipt was recovered from the server, that value is
        /// the recovered purchase — the one the caller started with cannot close anything.
        /// </param>
        /// <param name="delivery">
        /// Confirmation from the game server after verification and idempotent delivery.
        /// Carries the authoritative closing data. Must not be null.
        /// </param>
        /// <param name="cancellationToken">Cancels the call.</param>
        public Task<ClosePurchaseOutcome> ClosePurchaseAsync(
            PendingPurchase pending,
            PurchaseDeliveryConfirmation delivery,
            CancellationToken cancellationToken = default) =>
            PurchaseClose.RunAsync(m_source, pending, delivery, cancellationToken);

        /// <summary>
        /// Recovers a missing receipt from open purchases for this order.
        /// </summary>
        /// <remarks>
        /// Uses the first matching product and preserves its original order metadata.
        /// An empty result is <see cref="PurchaseBusinessOutcome.NothingToRestore"/>.
        /// Retry after the player finishes external payment; this method does not poll.
        /// </remarks>
        private async Task<(PreparePurchaseOutcome Outcome, PendingPurchase Pending)> RestorePendingAsync(
            IPaymentsService payments, PendingPurchase pending, ApiCallContext context)
        {
            var order = pending.Order;
            var restore = await payments.RestorePurchasesAsync(
                new PurchaseRestoreRequest
                {
                    ProviderId = PurchaseProviders.ToRestore(pending.Market),
                    Country = order.Country,
                    Language = order.Language,
                    ServerId = order.ServerId,
                    AppVersion = order.AppVersion,
                },
                context);

            var restoreClass = SdkResultClassification.Of(restore);
            if (restoreClass.Kind != SdkResultKind.Success)
            {
                return (PreparePurchaseOutcome.Translate(
                    PreparePurchaseStep.Restore, restoreClass, PurchaseOutcomeMap.Of(restore),
                    pending), null);
            }

            var entries = (restore as PaymentsRestorePurchasesResult.Success)?.Data?.Restores;
            RestorePurchase match = null;
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry != null && entry.ProductId == order.ProductId)
                    {
                        match = entry;
                        break;
                    }
                }
            }

            if (match == null || string.IsNullOrEmpty(match.AxylReceipt))
            {
                return (PreparePurchaseOutcome.Business(
                    PreparePurchaseStep.Restore, PurchaseBusinessOutcome.NothingToRestore,
                    restore.RawResponse, pending), null);
            }

            // Preserve the restored order's price, currency, and payload.
            var restoredOrder = new PurchaseOrder(
                match.ProductId,
                match.Price ?? order.Price,
                match.Currency ?? order.Currency,
                order.Country,
                order.Language,
                order.StorePlayerId,
                order.ServerId,
                match.IapPayload ?? order.IapPayload,
                order.AppVersion,
                order.AccountUuid);

            return (null, new PendingPurchase(
                match.AxylReceipt, match.OrderId, restoredOrder, pending.Market,
                storeTransactionId: match.StoreTransactionId));
        }

        /// <summary>
        /// Turns a classified SDK result into the starting Recipe's outcome. Only the
        /// <see cref="SdkResultKind.TypedOutcome"/> branch consults <paramref name="businessOutcome"/>;
        /// the rest carry everything they need already.
        /// </summary>
        private static InitiatePurchaseOutcome TranslateInitiate(
            InitiatePurchaseStep step,
            SdkResultClassification classification,
            PurchaseBusinessOutcome businessOutcome)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UserCanceled:
                case SdkResultKind.UntypedProblem:
                    // No step here opens a UI there is anything to dismiss, so a cancellation is the
                    // SDK reporting one rather than a player declining, and this Recipe has no
                    // separate state for it.
                    return InitiatePurchaseOutcome.Failed(
                        step,
                        classification.Problem ?? new HiveError(
                            HiveErrorCode.Cancelled, "The SDK reported the call as canceled."));

                case SdkResultKind.UnknownOutcome:
                    return InitiatePurchaseOutcome.Unrecognized(
                        step, classification.UnknownCode, classification.RawJson);

                default:
                    // A typed Outcome this Recipe has no value for stays unrecognized rather than
                    // being folded into a neighbouring meaning.
                    return businessOutcome == PurchaseBusinessOutcome.Unrecognized
                        ? InitiatePurchaseOutcome.Unrecognized(step, string.Empty, classification.RawJson)
                        : InitiatePurchaseOutcome.Business(step, businessOutcome, classification.RawJson);
            }
        }

    }
}

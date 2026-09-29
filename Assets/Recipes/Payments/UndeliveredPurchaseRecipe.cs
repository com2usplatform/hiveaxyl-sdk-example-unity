// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Payments;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>Prepares a recovered purchase for game-server verification.</summary>
    /// <remarks>The client prepares purchase data and closes only after the game server verifies
    /// the receipt and confirms idempotent delivery. Retain pending purchases on failure or cancellation.
    /// The game server owns verification, account/product/price checks, and entitlement eligibility.</remarks>
    public sealed class UndeliveredPurchaseRecipe
    {
        private readonly IStorePurchaseSource m_source;

        /// <summary>Creates the Recipe for one market.</summary>
        /// <param name="source">The market whose purchases are being recovered. Must not be null.</param>
        public UndeliveredPurchaseRecipe(IStorePurchaseSource source)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>
        /// Finds open purchases for the selected market: Steam and PG query the server;
        /// Apple and Google query the store.
        /// </summary>
        /// <remarks>
        /// Server results carry the restored order's metadata. Store results may lack price and currency;
        /// supply the original purchase metadata before verification.
        /// An empty result means no open purchases were found in this query's scope.
        /// </remarks>
        /// <param name="query">The player's locale and scope. Must not be null.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        public async Task<FindUndeliveredOutcome> FindUndeliveredAsync(
            UndeliveredQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null
                || string.IsNullOrWhiteSpace(query.Country)
                || string.IsNullOrWhiteSpace(query.Language))
            {
                return FindUndeliveredOutcome.Failed(
                    FindUndeliveredStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "Finding undelivered purchases needs a country and a language. "
                        + "Nothing was sent."));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return FindUndeliveredOutcome.Failed(
                    FindUndeliveredStep.Validate,
                    new HiveError(HiveErrorCode.Cancelled, "The caller canceled before the call."));
            }

            // A null store result selects server-side recovery.
            var store = await m_source.FindOpenPurchasesAsync(cancellationToken);
            if (store != null)
            {
                if (!store.IsSuccess)
                {
                    return store.Error != null
                        ? FindUndeliveredOutcome.Failed(FindUndeliveredStep.Store, store.Error)
                        : FindUndeliveredOutcome.Unrecognized(
                            FindUndeliveredStep.Store, store.UnknownOutcomeCode, store.RawJson);
                }

                return FindUndeliveredOutcome.Succeeded(FromStore(store.Purchases, query), string.Empty);
            }

            if (!HiveCore.TryResolve<IPaymentsService>(out var payments))
            {
                return FindUndeliveredOutcome.Failed(
                    FindUndeliveredStep.Resolve,
                    new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "The payments Capability is not registered. Initialize the SDK with AddPayments."));
            }

            var restore = await payments.RestorePurchasesAsync(
                new PurchaseRestoreRequest
                {
                    ProviderId = PurchaseProviders.ToRestore(m_source.Market),
                    Country = query.Country,
                    Language = query.Language,
                    ServerId = query.ServerId,
                    AppVersion = query.AppVersion,
                },
                new ApiCallContext { Token = cancellationToken });

            var classification = SdkResultClassification.Of(restore);
            if (classification.Kind != SdkResultKind.Success)
            {
                return TranslateFind(classification, PurchaseOutcomeMap.Of(restore));
            }

            var entries = (restore as PaymentsRestorePurchasesResult.Success)?.Data?.Restores;
            return FindUndeliveredOutcome.Succeeded(FromRestore(entries, query), restore.RawResponse);
        }

        /// <summary>
        /// Maps the store's own open purchases into pending ones, with the query's locale and no
        /// price: the store's record does not carry one, and inventing one would send the game server a
        /// number nobody agreed to. The caller fills it from the catalog the player bought
        /// against, before game-server verification.
        /// </summary>
        private IReadOnlyList<PendingPurchase> FromStore(
            IReadOnlyList<StoreOpenPurchase> entries, UndeliveredQuery query)
        {
            var found = new List<PendingPurchase>();
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var order = new PurchaseOrder(
                    entry.ProductId,
                    0m,
                    string.Empty,
                    query.Country,
                    query.Language,
                    storePlayerId: 0L,
                    query.ServerId,
                    iapPayload: null,
                    query.AppVersion,
                    query.AccountUuid);

                found.Add(new PendingPurchase(
                    entry.AxylReceipt, orderId: null, order, m_source.Market,
                    entry.VerifyToken, entry.FinishToken, entry.StoreTransactionId,
                    entry.StoreVerificationError));
            }

            return found;
        }

        /// <summary>
        /// Maps recovered entries using their original order metadata and the query locale.
        /// Null entries are skipped.
        /// </summary>
        private IReadOnlyList<PendingPurchase> FromRestore(
            IReadOnlyList<RestorePurchase> entries, UndeliveredQuery query)
        {
            var undelivered = new List<PendingPurchase>();
            if (entries == null)
            {
                return undelivered;
            }

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var order = new PurchaseOrder(
                    entry.ProductId,
                    entry.Price ?? 0m,
                    entry.Currency ?? string.Empty,
                    query.Country,
                    query.Language,
                    storePlayerId: 0L,
                    query.ServerId,
                    entry.IapPayload,
                    query.AppVersion,
                    query.AccountUuid);

                undelivered.Add(new PendingPurchase(
                    entry.AxylReceipt, entry.OrderId, order, m_source.Market,
                    storeTransactionId: entry.StoreTransactionId));
            }

            return undelivered;
        }

        /// <summary>Prepares a recovered purchase for game-server verification.
        /// Success only means preparation completed. The game server must verify and grant before closing.
        /// Preserve the returned Pending, including its receipt, across retries.</summary>
        /// <remarks>Apple/Google record purchase data. Steam/PG validate the restored data locally
        /// without a payment-server request. This method does not select a verification request kind.</remarks>
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
                        "Preparing a recovered purchase needs the purchase the find handed back. "
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
                        $"The purchase names {pending.Market} but this Recipe recovers "
                        + $"{m_source.Market} purchases. Prepare it with its own market's Recipe. "
                        + "Nothing was sent."),
                    pending);
            }

            // Every restored entry should carry the receipt the server sealed. One without it cannot
            // be verified by anyone, so it is reported here rather than sent to fail opaquely.
            if (string.IsNullOrWhiteSpace(pending.AxylReceipt))
            {
                return PreparePurchaseOutcome.Failed(
                    PreparePurchaseStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "The recovered purchase needs a receipt for preparation."),
                    pending);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return PreparePurchaseOutcome.Failed(
                    PreparePurchaseStep.Validate,
                    new HiveError(HiveErrorCode.Cancelled, "The caller canceled before the call."),
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

            // Record recovered store purchases before verification.
            // Preparation alone never authorizes delivery; the game server must verify and grant idempotently.
            if (pending.Market == PurchaseMarket.Apple || pending.Market == PurchaseMarket.Google)
            {
                var record = await payments.RecordStorePurchaseAsync(
                    pending.ToRecordRequest(), new ApiCallContext { Token = cancellationToken });

                var recordClass = SdkResultClassification.Of(record);
                if (recordClass.Kind != SdkResultKind.Success
                    && !(record is PaymentsRecordStorePurchaseResult.VerifyError))
                {
                    return PreparePurchaseOutcome.Translate(
                        PreparePurchaseStep.Record, recordClass, PurchaseOutcomeMap.Of(record), pending);
                }
            }

            return PreparePurchaseOutcome.Succeeded(pending);
        }

        /// <summary>
        /// Closes a recovered purchase the game server has granted — the same shared closing step a
        /// fresh purchase gets, so a recovered purchase closes exactly the way a fresh one does.
        /// </summary>
        /// <param name="pending">What <see cref="PreparePurchaseAsync"/> handed back. Must not be null.</param>
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

        private static FindUndeliveredOutcome TranslateFind(
            SdkResultClassification classification, PurchaseBusinessOutcome businessOutcome)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UserCanceled:
                case SdkResultKind.UntypedProblem:
                    return FindUndeliveredOutcome.Failed(
                        FindUndeliveredStep.Restore,
                        classification.Problem ?? new HiveError(
                            HiveErrorCode.Cancelled, "The SDK reported the call as canceled."));

                case SdkResultKind.UnknownOutcome:
                    return FindUndeliveredOutcome.Unrecognized(
                        FindUndeliveredStep.Restore, classification.UnknownCode, classification.RawJson);

                default:
                    return businessOutcome == PurchaseBusinessOutcome.Unrecognized
                        ? FindUndeliveredOutcome.Unrecognized(
                            FindUndeliveredStep.Restore, string.Empty, classification.RawJson)
                        : FindUndeliveredOutcome.Business(
                            FindUndeliveredStep.Restore, businessOutcome, classification.RawJson);
            }
        }
    }
}

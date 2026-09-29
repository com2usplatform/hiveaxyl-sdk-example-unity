// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Payments;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>Saves the subscription for game-server verification.</summary>
    /// <remarks>The client prepares purchase data and closes only after the game server verifies
    /// the receipt and confirms idempotent delivery. Retain pending purchases on failure or cancellation.
    /// The game server owns verification, account/product/price checks, and entitlement eligibility.</remarks>
    public sealed class SubscriptionRecipe
    {
        private readonly ISubscriptionSource m_source;

        /// <summary>Creates the Recipe for one market.</summary>
        /// <param name="source">
        /// The subscription source. Must not be null. Supported by Apple and Google.
        /// </param>
        public SubscriptionRecipe(ISubscriptionSource source)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>
        /// Records the intent at the server and has the store take the subscription payment.
        /// </summary>
        /// <param name="order">What the player chose. Must not be null.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        public async Task<StartSubscriptionOutcome> StartSubscriptionAsync(
            PurchaseOrder order, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return StartSubscriptionOutcome.Failed(
                    StartSubscriptionStep.Validate,
                    new HiveError(HiveErrorCode.Cancelled, "The caller canceled before the call."));
            }

            if (order == null || string.IsNullOrWhiteSpace(order.ProductId))
            {
                return StartSubscriptionOutcome.Failed(
                    StartSubscriptionStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "A subscription needs a product id. Nothing was sent."));
            }

            if (!HiveCore.TryResolve<IPaymentsService>(out var payments))
            {
                return StartSubscriptionOutcome.Failed(
                    StartSubscriptionStep.Resolve,
                    new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "The payments Capability is not registered. Initialize the SDK with AddPayments."));
            }

            // Record purchase intent before starting the store transaction.
            var pre = await payments.PrepareSubscriptionAsync(
                new SubscriptionPrePurchaseRequest
                {
                    ProviderId = ToPrepareProvider(m_source.Market),
                    ProductId = order.ProductId,
                    Price = order.Price,
                    Currency = order.Currency,
                    Country = order.Country,
                    Language = order.Language,
                    ServerId = order.ServerId,
                    IapPayload = order.IapPayload,
                    AccountUuid = order.AccountUuid,
                    AppVersion = order.AppVersion,
                },
                new ApiCallContext { Token = cancellationToken });

            var preClass = SdkResultClassification.Of(pre);
            if (preClass.Kind != SdkResultKind.Success)
            {
                return TranslateStart(
                    StartSubscriptionStep.Prepare, preClass, PurchaseOutcomeMap.Of(pre));
            }

            var store = await m_source.PurchaseAsync(order, cancellationToken);
            switch (store.Status)
            {
                case StorePurchaseStatus.ReceiptReady:
                    break;

                case StorePurchaseStatus.UserCanceled:
                    return StartSubscriptionOutcome.Canceled();

                case StorePurchaseStatus.BusinessOutcome:
                    return store.BusinessOutcome == PurchaseBusinessOutcome.Unrecognized
                        ? StartSubscriptionOutcome.Unrecognized(
                            StartSubscriptionStep.StorePurchase, store.UnknownOutcomeCode, store.RawJson)
                        : StartSubscriptionOutcome.Business(
                            StartSubscriptionStep.StorePurchase, store.BusinessOutcome, store.RawJson);

                default:
                    return StartSubscriptionOutcome.Failed(
                        StartSubscriptionStep.StorePurchase,
                        store.Error ?? new HiveError(
                            HiveErrorCode.Internal, "The store failed without saying why."));
            }

            if (string.IsNullOrEmpty(store.AxylReceipt))
            {
                return StartSubscriptionOutcome.Failed(
                    StartSubscriptionStep.StorePurchase,
                    new HiveError(
                        HiveErrorCode.Internal,
                        "The store reported a subscription without a receipt, so it cannot be saved."));
            }

            return StartSubscriptionOutcome.Succeeded(new PendingPurchase(
                store.AxylReceipt, store.OrderId, order, m_source.Market,
                store.VerifyToken, store.FinishToken, store.StoreTransactionId,
                store.StoreVerificationError));
        }

        /// <summary>Saves the subscription for game-server verification.
        /// Success only means preparation completed. The game server must verify and grant before closing.
        /// Preserve the returned Pending, including any recovered receipt, across retries.</summary>
        /// <param name="pending">The pending purchase; must belong to this market.</param>
        /// <param name="cancellationToken">Cancels preparation.</param>
        public async Task<SaveSubscriptionOutcome> SaveSubscriptionAsync(
            PendingPurchase pending, CancellationToken cancellationToken = default)
        {
            if (pending == null || pending.Order == null
                || string.IsNullOrWhiteSpace(pending.AxylReceipt))
            {
                return SaveSubscriptionOutcome.Failed(
                    SaveSubscriptionStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "Saving a subscription needs the pending subscription the start handed "
                        + "back, receipt included. Nothing was sent."),
                    pending);
            }

            // The provider every call names comes from the pending subscription — a mismatch would
            // save and verify against the wrong market.
            if (pending.Market != m_source.Market)
            {
                return SaveSubscriptionOutcome.Failed(
                    SaveSubscriptionStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        $"The subscription names {pending.Market} but this Recipe subscribes at "
                        + $"{m_source.Market}. Prepare it with its own market's Recipe. "
                        + "Nothing was sent."),
                    pending);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return SaveSubscriptionOutcome.Failed(
                    SaveSubscriptionStep.Validate,
                    new HiveError(HiveErrorCode.Cancelled, "The caller canceled before the calls."),
                    pending);
            }

            if (!HiveCore.TryResolve<IPaymentsService>(out var payments))
            {
                return SaveSubscriptionOutcome.Failed(
                    SaveSubscriptionStep.Resolve,
                    new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "The payments Capability is not registered. Initialize the SDK with AddPayments."),
                    pending);
            }

            var order = pending.Order;
            var context = new ApiCallContext { Token = cancellationToken };

            var save = await payments.PurchaseSubscriptionAsync(
                new SubscriptionPurchaseRequest
                {
                    ProviderId = ToSaveProvider(pending.Market),
                    AxylReceipt = pending.AxylReceipt,
                    StoreTransactionId = pending.StoreTransactionId,
                    Price = order.Price,
                    Currency = order.Currency,
                    Country = order.Country,
                    Language = order.Language,
                    ServerId = order.ServerId,
                    AppVersion = order.AppVersion,
                },
                context);

            var saveClass = SdkResultClassification.Of(save);

            // A subscription already saved answers as a conflict, and a rerun is exactly when that
            // happens after an interrupted confirm. The game server verifies eligibility and grants
            // idempotently after preparation; any other refusal stops here.
            if (saveClass.Kind != SdkResultKind.Success
                && !(save is PaymentsPurchaseSubscriptionResult.PaymentResourceConflict))
            {
                return TranslateSave(
                    SaveSubscriptionStep.Save, saveClass, PurchaseOutcomeMap.Of(save), pending);
            }

            return SaveSubscriptionOutcome.Succeeded(pending);
        }

        /// <summary>
        /// Confirms a subscription after game-server verification and delivery, then closes it at the store.
        /// </summary>
        /// <remarks>
        /// Check the confirmation outcome. On failure, retain the pending subscription and retry
        /// confirmation without granting again. Already-confirmed purchases still complete store closing.
        /// </remarks>
        /// <param name="pending">What <see cref="StartSubscriptionAsync"/> handed back. Must not be null.</param>
        /// <param name="delivery">
        /// Confirmation from the game server after verification and idempotent delivery.
        /// Carries the authoritative closing data. Must not be null.
        /// </param>
        /// <param name="cancellationToken">Cancels the call.</param>
        public async Task<ConfirmSubscriptionOutcome> ConfirmSubscriptionAsync(
            PendingPurchase pending,
            SubscriptionDeliveryConfirmation delivery,
            CancellationToken cancellationToken = default)
        {
            if (pending == null || pending.Order == null
                || string.IsNullOrWhiteSpace(pending.AxylReceipt))
            {
                return ConfirmSubscriptionOutcome.Failed(
                    ConfirmSubscriptionStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "Confirming a subscription needs the pending subscription the start handed "
                        + "back, receipt included. Nothing was sent."),
                    pending);
            }

            // Guarded like the save — and here the silent shape is worse: a foreign market's
            // source answers null for the store close, so Apple's transaction would never be
            // finished while the outcome still read as Success.
            if (pending.Market != m_source.Market)
            {
                return ConfirmSubscriptionOutcome.Failed(
                    ConfirmSubscriptionStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        $"The subscription names {pending.Market} but this Recipe subscribes at "
                        + $"{m_source.Market}. Confirm it with its own market's Recipe. "
                        + "Nothing was sent."),
                    pending);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return ConfirmSubscriptionOutcome.Failed(
                    ConfirmSubscriptionStep.Validate,
                    new HiveError(HiveErrorCode.Cancelled, "The caller canceled before the call."),
                    pending);
            }

            if (delivery == null)
            {
                return ConfirmSubscriptionOutcome.Failed(
                    ConfirmSubscriptionStep.Validate,
                    new HiveError(HiveErrorCode.InvalidArgument,
                        "Game-server verification and delivery confirmation are required."), pending);
            }

            if (!HiveCore.TryResolve<IPaymentsService>(out var payments))
            {
                return ConfirmSubscriptionOutcome.Failed(
                    ConfirmSubscriptionStep.Resolve,
                    new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "The payments Capability is not registered. Initialize the SDK with AddPayments."),
                    pending);
            }

            var order = pending.Order;

            // Use the same receipt supplied when saving the subscription.
            var confirm = await payments.PostSubscriptionAsync(
                new SubscriptionPurchasePostRequest
                {
                    ProviderId = ToConfirmProvider(pending.Market),
                    AxylReceipt = pending.AxylReceipt,
                    ProductId = string.IsNullOrEmpty(delivery?.ProductId)
                        ? order.ProductId
                        : delivery.ProductId,
                    Currency = order.Currency,
                    Country = order.Country,
                    Language = order.Language,
                    ServerId = order.ServerId,
                    AppVersion = order.AppVersion,
                },
                new ApiCallContext { Token = cancellationToken });

            var confirmClass = SdkResultClassification.Of(confirm);
            var alreadyConfirmed = confirm is PaymentsPostSubscriptionResult.VerifyDuplicated;
            if (confirmClass.Kind != SdkResultKind.Success && !alreadyConfirmed)
            {
                return TranslateConfirm(
                    ConfirmSubscriptionStep.Confirm, confirmClass,
                    PurchaseOutcomeMap.Of(confirm), pending);
            }

            var confirmedId = (confirm as PaymentsPostSubscriptionResult.Success)
                ?.Data?.HiveAxylTransactionId;

            // Treat a missing confirmation transaction ID as a failure.
            if (!alreadyConfirmed && string.IsNullOrEmpty(confirmedId))
            {
                return ConfirmSubscriptionOutcome.Failed(
                    ConfirmSubscriptionStep.Confirm,
                    new HiveError(
                        HiveErrorCode.Internal,
                        "The server confirmed nothing: no saved subscription matched this receipt. "
                        + "Save the subscription first, with the same receipt."),
                    pending);
            }

            // Finish the store transaction only after confirmation succeeds.
            var storeClose = await m_source.FinishAtStoreAsync(pending, cancellationToken);
            if (storeClose != null)
            {
                var closeClass = SdkResultClassification.Of(storeClose);
                if (closeClass.Kind != SdkResultKind.Success)
                {
                    return TranslateConfirm(
                        ConfirmSubscriptionStep.Confirm, closeClass,
                        PurchaseOutcomeMap.Of(storeClose), pending);
                }
            }

            return ConfirmSubscriptionOutcome.Succeeded(pending, confirmedId);
        }

        private static SubscriptionPrePurchaseRequestProviderId ToPrepareProvider(PurchaseMarket market) =>
            market == PurchaseMarket.Google
                ? SubscriptionPrePurchaseRequestProviderId.Google
                : SubscriptionPrePurchaseRequestProviderId.Apple;

        private static SubscriptionPurchaseRequestProviderId ToSaveProvider(PurchaseMarket market) =>
            market == PurchaseMarket.Google
                ? SubscriptionPurchaseRequestProviderId.Google
                : SubscriptionPurchaseRequestProviderId.Apple;

        private static SubscriptionPurchasePostRequestProviderId ToConfirmProvider(PurchaseMarket market) =>
            market == PurchaseMarket.Google
                ? SubscriptionPurchasePostRequestProviderId.Google
                : SubscriptionPurchasePostRequestProviderId.Apple;

        private static StartSubscriptionOutcome TranslateStart(
            StartSubscriptionStep step,
            SdkResultClassification classification,
            PurchaseBusinessOutcome businessOutcome)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UserCanceled:
                case SdkResultKind.UntypedProblem:
                    return StartSubscriptionOutcome.Failed(
                        step,
                        classification.Problem ?? new HiveError(
                            HiveErrorCode.Cancelled, "The SDK reported the call as canceled."));

                case SdkResultKind.UnknownOutcome:
                    return StartSubscriptionOutcome.Unrecognized(
                        step, classification.UnknownCode, classification.RawJson);

                default:
                    return businessOutcome == PurchaseBusinessOutcome.Unrecognized
                        ? StartSubscriptionOutcome.Unrecognized(step, string.Empty, classification.RawJson)
                        : StartSubscriptionOutcome.Business(step, businessOutcome, classification.RawJson);
            }
        }

        private static SaveSubscriptionOutcome TranslateSave(
            SaveSubscriptionStep step,
            SdkResultClassification classification,
            PurchaseBusinessOutcome businessOutcome,
            PendingPurchase pending)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UserCanceled:
                case SdkResultKind.UntypedProblem:
                    return SaveSubscriptionOutcome.Failed(
                        step,
                        classification.Problem ?? new HiveError(
                            HiveErrorCode.Cancelled, "The SDK reported the call as canceled."),
                        pending);

                case SdkResultKind.UnknownOutcome:
                    return SaveSubscriptionOutcome.Unrecognized(
                        step, classification.UnknownCode, classification.RawJson, pending);

                default:
                    return businessOutcome == PurchaseBusinessOutcome.Unrecognized
                        ? SaveSubscriptionOutcome.Unrecognized(
                            step, string.Empty, classification.RawJson, pending)
                        : SaveSubscriptionOutcome.Business(
                            step, businessOutcome, classification.RawJson, pending);
            }
        }

        private static ConfirmSubscriptionOutcome TranslateConfirm(
            ConfirmSubscriptionStep step,
            SdkResultClassification classification,
            PurchaseBusinessOutcome businessOutcome,
            PendingPurchase pending)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UserCanceled:
                case SdkResultKind.UntypedProblem:
                    return ConfirmSubscriptionOutcome.Failed(
                        step,
                        classification.Problem ?? new HiveError(
                            HiveErrorCode.Cancelled, "The SDK reported the call as canceled."),
                        pending);

                case SdkResultKind.UnknownOutcome:
                    return ConfirmSubscriptionOutcome.Unrecognized(
                        step, classification.UnknownCode, classification.RawJson, pending);

                default:
                    return businessOutcome == PurchaseBusinessOutcome.Unrecognized
                        ? ConfirmSubscriptionOutcome.Unrecognized(
                            step, string.Empty, classification.RawJson, pending)
                        : ConfirmSubscriptionOutcome.Business(
                            step, businessOutcome, classification.RawJson, pending);
            }
        }
    }
}

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Payments;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// The one close both purchase Recipes share: finalize at the server for the markets that close
    /// there, or the market's own finish for the ones that close at the device.
    /// </summary>
    internal static class PurchaseClose
    {
        /// <summary>
        /// Closes a verified purchase the game server has granted. Runs after the grant on purpose:
        /// the close is what consumes the order, and a restore only finds unconsumed ones.
        /// </summary>
        internal static async Task<ClosePurchaseOutcome> RunAsync(
            IStorePurchaseSource source,
            PendingPurchase pending,
            PurchaseDeliveryConfirmation delivery,
            CancellationToken cancellationToken)
        {
            if (pending == null || pending.Order == null)
            {
                return ClosePurchaseOutcome.Failed(
                    ClosePurchaseStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "Closing a purchase needs the prepared pending purchase. "
                        + "Nothing was sent."),
                    pending);
            }

            // The provider every call names comes from the pending purchase, and the close's own
            // half comes from the source — a mismatch would close against the wrong market.
            if (pending.Market != source.Market)
            {
                return ClosePurchaseOutcome.Failed(
                    ClosePurchaseStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        $"The purchase names {pending.Market} but this Recipe closes "
                        + $"{source.Market} purchases. Close it with its own market's Recipe. "
                        + "Nothing was sent."),
                    pending);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return ClosePurchaseOutcome.Failed(
                    ClosePurchaseStep.Validate,
                    new HiveError(HiveErrorCode.Cancelled, "The caller canceled before the call."),
                    pending);
            }

            if (delivery == null)
            {
                return ClosePurchaseOutcome.Failed(
                    ClosePurchaseStep.Validate,
                    new HiveError(HiveErrorCode.InvalidArgument,
                        "Game-server verification and delivery confirmation are required."), pending);
            }

            if (!HiveCore.TryResolve<IPaymentsService>(out var payments))
            {
                return ClosePurchaseOutcome.Failed(
                    ClosePurchaseStep.Resolve,
                    new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "The payments Capability is not registered. Initialize the SDK with AddPayments."),
                    pending);
            }

            // Use the game server's refreshed receipt when provided; otherwise retain the original.
            var finalizeReceipt = string.IsNullOrEmpty(delivery?.Receipt)
                ? pending.AxylReceipt
                : delivery.Receipt;

            // Whose call this is depends on the market. Steam and PG close at the server; Google
            // consumes and Apple finishes at the store, and skipping that is not cosmetic — an
            // unconsumed Google purchase cannot be bought again and is refunded once its window passes.
            var finalize = await source.FinishAsync(pending, finalizeReceipt, cancellationToken);
            if (finalize == null)
            {
                // Require a receipt before server-side closing.
                if (string.IsNullOrEmpty(finalizeReceipt))
                {
                    return ClosePurchaseOutcome.Failed(
                        ClosePurchaseStep.Validate,
                        new HiveError(
                            HiveErrorCode.InvalidArgument,
                            "Closing at the server needs a receipt, and neither the game-server response nor "
                            + "the pending purchase carries one. Prepare the purchase and request "
                            + "game-server verification first."),
                        pending);
                }

                finalize = await payments.FinalizePurchaseAsync(
                    new PurchaseFinalizeRequest
                    {
                        AxylReceipt = finalizeReceipt,
                        ProviderId = PurchaseProviders.ToFinalize(pending.Market),
                    },
                    new ApiCallContext { Token = cancellationToken });
            }

            var finalizeClass = SdkResultClassification.Of(finalize);
            if (finalizeClass.Kind != SdkResultKind.Success)
            {
                return Translate(
                    ClosePurchaseStep.Finalize, finalizeClass, PurchaseOutcomeMap.Of(finalize), pending);
            }

            var closed = finalize as PaymentsFinalizePurchaseResult.Success;
            return ClosePurchaseOutcome.Succeeded(
                pending, closed?.Data.OrderId, closed?.Data.StoreTransactionId);
        }

        /// <summary>
        /// Turns a classified SDK result into the closing step's outcome. The pending purchase
        /// travels through every branch, because a caller that loses the receipt loses the purchase.
        /// </summary>
        private static ClosePurchaseOutcome Translate(
            ClosePurchaseStep step,
            SdkResultClassification classification,
            PurchaseBusinessOutcome businessOutcome,
            PendingPurchase pending)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UserCanceled:
                case SdkResultKind.UntypedProblem:
                    return ClosePurchaseOutcome.Failed(
                        step,
                        classification.Problem ?? new HiveError(
                            HiveErrorCode.Cancelled, "The SDK reported the call as canceled."),
                        pending);

                case SdkResultKind.UnknownOutcome:
                    return ClosePurchaseOutcome.Unrecognized(
                        step, classification.UnknownCode, classification.RawJson, pending);

                default:
                    return businessOutcome == PurchaseBusinessOutcome.Unrecognized
                        ? ClosePurchaseOutcome.Unrecognized(step, string.Empty, classification.RawJson, pending)
                        : ClosePurchaseOutcome.Business(step, businessOutcome, classification.RawJson, pending);
            }
        }
    }
}

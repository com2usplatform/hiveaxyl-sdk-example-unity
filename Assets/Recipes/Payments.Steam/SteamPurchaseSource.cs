// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Payments;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Steam's half of a consumable purchase: the server creates the order, and Steam asks the player
    /// to approve it.
    /// </summary>
    /// <remarks>
    /// Initiation returns before the player approves payment.
    /// Wait for the matching Steam approval callback before requesting game-server verification and delivery.
    /// After delivery, close through the Recipe's <c>ClosePurchaseAsync</c>.
    /// </remarks>
    public sealed class SteamPurchaseSource : IStorePurchaseSource
    {
        /// <inheritdoc />
        public PurchaseMarket Market => PurchaseMarket.Steam;

        /// <inheritdoc />
        public bool RecordsIntentFirst => false;

        /// <inheritdoc />
        /// <remarks>
        /// Steam's own purchase call answers with the receipt, so there is nothing to recover.
        /// </remarks>
        public bool RecoversPendingFromServer => false;

        /// <inheritdoc />
        public async Task<StorePurchaseResult> PurchaseAsync(
            PurchaseOrder order, CancellationToken cancellationToken)
        {
            if (!HiveCore.TryResolve<IPaymentsService>(out var payments))
            {
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The payments Capability is not registered. Initialize the SDK with AddPayments."));
            }

            var result = await payments.InitiatePurchaseAsync(
                new PurchaseInitRequest
                {
                    ProviderId = PurchaseInitRequestProviderId.Steam,
                    ProductId = order.ProductId,
                    Country = order.Country,
                    Currency = order.Currency,
                    Language = order.Language,
                    StorePlayerId = order.StorePlayerId,
                    ServerId = order.ServerId,
                    AppVersion = order.AppVersion,
                    IapPayload = order.IapPayload,
                },
                new ApiCallContext { Token = cancellationToken });

            var classification = SdkResultClassification.Of(result);
            if (classification.Kind != SdkResultKind.Success)
            {
                return StoreResultFrom(classification, result);
            }

            if (!(result is PaymentsInitiatePurchaseResult.Success ok))
            {
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.Internal,
                    "The purchase call reported success without a success payload."));
            }

            if (string.IsNullOrEmpty(ok.Data.AxylReceipt))
            {
                // Steam needs this receipt for every later step.
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.Internal, "The server issued a Steam order without a receipt."));
            }

            return StorePurchaseResult.Ready(
                ok.Data.AxylReceipt,
                orderId: ok.Data.OrderId,
                storeTransactionId: ok.Data.StoreTransactionId);
        }

        /// <inheritdoc />
        public Task<IAxylResult> FinishAsync(
            PendingPurchase pending, string finalizeReceipt, CancellationToken cancellationToken) =>
            Task.FromResult<IAxylResult>(null);

        /// <inheritdoc />
        /// <remarks>
        /// Steam open purchases are recovered from the server.
        /// </remarks>
        public Task<StoreOpenPurchasesResult> FindOpenPurchasesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<StoreOpenPurchasesResult>(null);

        private static StorePurchaseResult StoreResultFrom(
            SdkResultClassification classification, IAxylResult result)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UserCanceled:
                    return StorePurchaseResult.Canceled();

                case SdkResultKind.UntypedProblem:
                    return StorePurchaseResult.Failed(classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    return StorePurchaseResult.Unrecognized(
                        classification.UnknownCode, classification.RawJson);

                default:
                    return StorePurchaseResult.Business(
                        PurchaseOutcomeMap.Of(result), classification.RawJson);
            }
        }
    }
}

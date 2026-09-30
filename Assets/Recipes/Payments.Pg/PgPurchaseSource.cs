// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Payments;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// PG's half of a consumable purchase: the server issues a payment page, and the player pays on it
    /// in a browser.
    /// </summary>
    /// <remarks>
    /// Open the returned payment URL and retain the pending purchase.
    /// Returning from the browser does not prove payment. Call <c>PreparePurchaseAsync</c>
    /// to recover the receipt, then request game-server verification and delivery before closing.
    /// </remarks>
    public sealed class PgPurchaseSource : IStorePurchaseSource
    {
        /// <summary>The smallest quantity accepted by a PG order.</summary>
        public const int k_MinQuantity = 1;

        /// <summary>The largest quantity accepted by a PG order.</summary>
        public const int k_MaxQuantity = 999;

        private readonly int m_quantity;

        /// <summary>Creates the source.</summary>
        /// <param name="quantity">
        /// How many of the product to buy. The order requires a quantity from 1 through 999; it is a
        /// parameter rather than a constant because the server accepts more than one per order, and one
        /// is the default a consumable purchase means.
        /// </param>
        public PgPurchaseSource(int quantity = 1)
        {
            m_quantity = quantity;
        }

        /// <inheritdoc />
        public PurchaseMarket Market => PurchaseMarket.Pg;

        /// <inheritdoc />
        /// <remarks>
        /// The one market that cannot report back at all, which makes the record the only trace the
        /// purchase leaves before the browser takes over.
        /// </remarks>
        public bool RecordsIntentFirst => true;

        /// <inheritdoc />
        /// <remarks>
        /// The receipt is recovered after external payment; initiation does not provide it.
        /// </remarks>
        public bool RecoversPendingFromServer => true;

        /// <inheritdoc />
        public async Task<StorePurchaseResult> PurchaseAsync(
            PurchaseOrder order, CancellationToken cancellationToken)
        {
            if (m_quantity < k_MinQuantity || m_quantity > k_MaxQuantity)
            {
                // Refused here rather than at the server, which would answer with a validation error
                // that does not name the field.
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.InvalidArgument,
                    $"A PG order must contain between {k_MinQuantity} and {k_MaxQuantity} items, not {m_quantity}."));
            }

            var os = PgOsCode.Current;
            if (os == null)
            {
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "A PG order has to name the client OS, and this platform has no value for it. "
                    + "PG is offered on Windows, macOS, Android, iOS and WebGL."));
            }

            if (!HiveCore.TryResolve<IPaymentsService>(out var payments))
            {
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The payments Capability is not registered. Initialize the SDK with AddPayments."));
            }

            var result = await payments.CreatePaymentUrlAsync(
                new OrderRequest
                {
                    ProviderId = OrderRequestProviderId.Pg,
                    ProductId = order.ProductId,
                    Quantity = m_quantity,
                    Os = os.Value,
                    Country = order.Country,
                    Language = order.Language,
                    ServerId = order.ServerId,
                    IapPayload = order.IapPayload,
                    AppVersion = order.AppVersion,
                },
                new ApiCallContext { Token = cancellationToken });

            var classification = SdkResultClassification.Of(result);
            if (classification.Kind != SdkResultKind.Success)
            {
                return StoreResultFrom(classification, result);
            }

            if (!(result is PaymentsCreatePaymentUrlResult.Success ok))
            {
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.Internal,
                    "The payment-url call reported success without a success payload."));
            }

            if (string.IsNullOrEmpty(ok.Data.PayUrl))
            {
                // Success with no page to send the player to. Reported rather than passed on, because a
                // caller opening an empty URL would look like the payment simply failed to appear.
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.Internal, "The server issued a PG order without a payment page URL."));
            }

            // No order id to carry: the response has none, and inventing one would imply the purchase
            // can be looked up later by something the client holds.
            return StorePurchaseResult.AwaitingExternal(ok.Data.PayUrl);
        }

        /// <inheritdoc />
        public Task<IAxylResult> FinishAsync(
            PendingPurchase pending, string finalizeReceipt, CancellationToken cancellationToken) =>
            // Nothing to close at the market — PG has none. Closing is the server's finalize call, which
            // the Recipe makes when a receipt exists. A PG purchase started here never has one.
            Task.FromResult<IAxylResult>(null);

        /// <inheritdoc />
        /// <remarks>
        /// PG open purchases are recovered from the server.
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

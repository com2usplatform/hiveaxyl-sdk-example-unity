// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Payments;
using Hive.Axyl.Payments.Addon.Google;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Google Play's half of a purchase — consumable or subscription. Play's own store UI takes the
    /// payment, and the close tells Play the item was granted: a consume for a consumable, an
    /// acknowledge for a subscription.
    /// </summary>
    /// <remarks>
    /// Requires the Google payments addon on Android.
    /// The source waits for a matching purchase event; opening the purchase sheet alone is not payment.
    /// Retain pending purchases for recovery after failure or cancellation.
    /// Complete closing only after game-server verification and delivery.
    /// </remarks>
    public sealed class GooglePurchaseSource : IStorePurchaseSource, ISubscriptionSource
    {
        /// <inheritdoc />
        public PurchaseMarket Market => PurchaseMarket.Google;

        /// <inheritdoc />
        /// <remarks>
        /// Play takes the payment on its own and can report it after the app has gone, so the record
        /// written first is what makes that purchase recognizable later.
        /// </remarks>
        public bool RecordsIntentFirst => true;

        /// <inheritdoc />
        /// <remarks>Play delivers the purchase to the app, receipt and all.</remarks>
        public bool RecoversPendingFromServer => false;

        /// <inheritdoc />
        /// <remarks>
        /// Buys the product as an in-app product. The same id bought through
        /// <see cref="ISubscriptionSource"/> is queried and launched as a subscription instead — the
        /// contract the caller holds is what names Play's product type.
        /// </remarks>
        public Task<StorePurchaseResult> PurchaseAsync(
            PurchaseOrder order, CancellationToken cancellationToken) =>
            PurchaseAsync(order, ProductType.Inapp, cancellationToken);

        /// <inheritdoc />
        Task<StorePurchaseResult> ISubscriptionSource.PurchaseAsync(
            PurchaseOrder order, CancellationToken cancellationToken) =>
            PurchaseAsync(order, ProductType.Subs, cancellationToken);

        private static async Task<StorePurchaseResult> PurchaseAsync(
            PurchaseOrder order, ProductType productType, CancellationToken cancellationToken)
        {
            if (!HiveCore.TryResolve<IGooglePlayBillingPlugin>(out var billing) || billing == null)
            {
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The Google Play Billing plugin is not registered. It resolves on Android only."));
            }

            var connected = await billing.StartConnectionAsync(cancellationToken);
            if (!IsConnected(connected))
            {
                return FromAddonFailure(connected, "connecting to Play Billing");
            }

            // Queried here rather than trusted to a catalog call the app may not have made: Play
            // refuses a product this session has not queried, and the subscription's offer token
            // exists nowhere but in this answer.
            var details = await billing.QueryProductDetailsAsync(
                new[] { order.ProductId }, productType, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.Cancelled, "The caller canceled before the sheet opened."));
            }

            if (!(details is GooglePlayBillingServiceQueryProductDetailsResult.Success priced))
            {
                return FromAddonFailure(details, "querying the product at Play");
            }

            var product = Find(priced.Data, order.ProductId);
            if (product == null)
            {
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.NotFound,
                    $"Play does not list {order.ProductId} as {Describe(productType)} product"
                    + $"{UnfetchedDetail(priced.Data, order.ProductId)}. Check the Play Console "
                    + "listing and the product type."));
            }

            // Subscribed before the sheet opens, because the purchase can land the moment it does —
            // and a subscription taken afterwards would miss it.
            var arrival = new TaskCompletionSource<GooglePurchase>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var refusal = new TaskCompletionSource<StorePurchaseResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void OnPurchasesUpdated(PurchasesUpdatedEventArgs args)
            {
                // Both are guaranteed non-null: PurchasesUpdatedEventArgs' constructor rejects a null
                // result and turns null purchases into an empty list.
                if (args.BillingResult.ResponseCode != GoogleBillingResponseCode.k_Ok)
                {
                    // The SDK passes Play's code through without branching, so the meanings are read
                    // here: a closed sheet is the player's choice and not a failure, and
                    // ITEM_ALREADY_OWNED means an earlier purchase was never consumed.
                    refusal.TrySetResult(FromResponseCode(
                        args.BillingResult.ResponseCode, args.BillingResult.DebugMessage));
                    return;
                }

                foreach (var purchase in args.Purchases)
                {
                    if (!Owns(purchase, order.ProductId))
                    {
                        continue;
                    }

                    if (purchase.PurchaseState != PurchaseState.Purchased)
                    {
                        // Pending — a cash top-up, say. Nothing is owed yet and granting now would give
                        // away an item that has not been paid for. Play sends another event later.
                        refusal.TrySetResult(StorePurchaseResult.Business(
                            PurchaseBusinessOutcome.StorePurchasePending,
                            $"the purchase is {purchase.PurchaseState} rather than purchased."));
                        return;
                    }

                    arrival.TrySetResult(purchase);
                    return;
                }
            }

            billing.PurchasesUpdated += OnPurchasesUpdated;
            try
            {
                var launched = await billing.LaunchBillingFlowAsync(
                    new LaunchBillingFlowRequest
                    {
                        Products = new[]
                        {
                            new LaunchBillingFlowProductParams
                            {
                                ProductId = order.ProductId,
                                OfferToken = OfferToken(product),
                            },
                        },

                        // Pass the account UUID when supplied.
                        ObfuscatedAccountId = string.IsNullOrEmpty(order.AccountUuid)
                            ? null
                            : order.AccountUuid,
                    },
                    cancellationToken);

                if (!(launched is GooglePlayBillingServiceLaunchBillingFlowResult.Success))
                {
                    return FromAddonFailure(launched, "opening the Play purchase sheet");
                }

                // Success meant the sheet opened. The purchase is what the event says.
                var canceled = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => canceled.TrySetResult(true)))
                {
                    var finished = await Task.WhenAny(
                        arrival.Task, refusal.Task, canceled.Task);

                    if (finished == canceled.Task)
                    {
                        // The sheet is Play's and stays open; the caller stopped waiting. The purchase
                        // may still complete, which is what recovery is for.
                        return StorePurchaseResult.Failed(new HiveError(
                            HiveErrorCode.Cancelled,
                            "The caller stopped waiting for Play. A purchase may still complete."));
                    }

                    if (finished == refusal.Task)
                    {
                        return refusal.Task.Result;
                    }
                }

                var bought = arrival.Task.Result;

                // The GPA order number rides twice: as the order id the consumable calls send, and
                // as the store transaction id — which the subscription contract requires from Google
                // (its save and verify have no order-id field to fall back to).
                return StorePurchaseResult.Ready(
                    ReceiptJson(bought),
                    verifyToken: bought.PurchaseToken,
                    finishToken: bought.PurchaseToken,
                    orderId: bought.OrderId,
                    storeTransactionId: bought.OrderId);
            }
            finally
            {
                billing.PurchasesUpdated -= OnPurchasesUpdated;
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Confirms the purchase before consuming it. An already-confirmed result also permits consumption.
        /// </remarks>
        public async Task<IAxylResult> FinishAsync(
            PendingPurchase pending, string finalizeReceipt, CancellationToken cancellationToken)
        {
            // Report missing store services as a failure; null would select server-side finalization.
            if (!HiveCore.TryResolve<IGooglePlayBillingPlugin>(out var billing) || billing == null)
            {
                return new GooglePlayBillingServiceConsumeResult.Failure(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The Google Play Billing plugin is not registered, so the purchase was not consumed. "
                    + "It cannot be bought again until it is."));
            }

            // The same refusal Apple gives an absent transaction id. A pending purchase built from
            // pasted fields carries no purchase token, and Play answers an empty consume with a
            // DeveloperError that says nothing about why — so it is named here instead.
            if (string.IsNullOrEmpty(pending.FinishToken))
            {
                return new GooglePlayBillingServiceConsumeResult.Failure(new HiveError(
                    HiveErrorCode.InvalidArgument,
                    "The purchase carries no Play purchase token, so there is nothing to consume. "
                    + "It was not started by this source."));
            }

            if (!HiveCore.TryResolve<IPaymentsService>(out var payments))
            {
                return new GooglePlayBillingServiceConsumeResult.Failure(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The payments Capability is not registered, so the purchase was not confirmed "
                    + "and not consumed."));
            }

            var confirm = await payments.RequestPurchaseAsync(
                new PurchasePostRequest
                {
                    ProviderId = PurchasePostRequestProviderId.Google,
                    AxylReceipt = pending.AxylReceipt,

                    ProductId = pending.Order?.ProductId,
                    StoreTransactionId = string.IsNullOrEmpty(pending.StoreTransactionId)
                        ? null
                        : pending.StoreTransactionId,
                },
                new ApiCallContext { Token = cancellationToken });

            var classification = SdkResultClassification.Of(confirm);
            if (classification.Kind != SdkResultKind.Success
                && !(confirm is PaymentsRequestPurchaseResult.VerifyDuplicated))
            {
                // Do not consume after a failed confirm: consumption removes the purchase
                // from Play's recovery list.
                return confirm;
            }

            var consumed = await billing.ConsumeAsync(pending.FinishToken, cancellationToken);

            // After confirmation, PurchaseNotFound means nothing remains to consume.
            return consumed is GooglePlayBillingServiceConsumeResult.PurchaseNotFound
                ? new GooglePlayBillingServiceConsumeResult.Success(new ConsumeResponse())
                : consumed;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Play keeps its open purchases at the device: <c>queryPurchasesAsync</c> lists every
        /// in-app purchase that was paid for and never consumed, which for a consumable is exactly
        /// the undelivered set. A pending one is left out — nothing is owed yet, and Play reports it
        /// again once the money arrives. The entries carry no price, so the caller supplies one
        /// where the verify call needs it, from the catalog the player bought against.
        /// </remarks>
        public async Task<StoreOpenPurchasesResult> FindOpenPurchasesAsync(
            CancellationToken cancellationToken)
        {
            if (!HiveCore.TryResolve<IGooglePlayBillingPlugin>(out var billing) || billing == null)
            {
                return StoreOpenPurchasesResult.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The Google Play Billing plugin is not registered. It resolves on Android only."));
            }

            var connected = await billing.StartConnectionAsync(cancellationToken);
            if (!IsConnected(connected))
            {
                return OpenPurchasesRefused(connected, "connecting to Play Billing");
            }

            var result = await billing.QueryPurchasesAsync(ProductType.Inapp, cancellationToken);
            if (!(result is GooglePlayBillingServiceQueryPurchasesResult.Success listed))
            {
                return OpenPurchasesRefused(result, "listing the open purchases");
            }

            var open = new List<StoreOpenPurchase>();
            foreach (var purchase in listed.Data?.Purchases ?? Array.Empty<GooglePurchase>())
            {
                if (purchase == null
                    || purchase.PurchaseState != PurchaseState.Purchased
                    || purchase.Products == null
                    || purchase.Products.Count == 0)
                {
                    continue;
                }

                // Preserve the receipt, verification token, finish token, and transaction ID.
                // This mapping uses the first product in the purchase.
                open.Add(new StoreOpenPurchase(
                    purchase.Products[0],
                    ReceiptJson(purchase),
                    verifyToken: purchase.PurchaseToken,
                    finishToken: purchase.PurchaseToken,
                    storeTransactionId: purchase.OrderId));
            }

            return StoreOpenPurchasesResult.Succeeded(open);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Acknowledges the subscription after confirmation. An already-acknowledged result is successful.
        /// </remarks>
        public async Task<IAxylResult> FinishAtStoreAsync(
            PendingPurchase pending, CancellationToken cancellationToken)
        {
            if (!HiveCore.TryResolve<IGooglePlayBillingPlugin>(out var billing) || billing == null)
            {
                return new GooglePlayBillingServiceAcknowledgePurchaseResult.Failure(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The Google Play Billing plugin is not registered, so the subscription was not "
                    + "acknowledged at the device."));
            }

            if (string.IsNullOrEmpty(pending.FinishToken))
            {
                return new GooglePlayBillingServiceAcknowledgePurchaseResult.Failure(new HiveError(
                    HiveErrorCode.InvalidArgument,
                    "The subscription carries no Play purchase token, so there is nothing to "
                    + "acknowledge. It was not started by this source."));
            }

            var acknowledged = await billing.AcknowledgePurchaseAsync(
                pending.FinishToken, cancellationToken);
            return acknowledged is GooglePlayBillingServiceAcknowledgePurchaseResult.AlreadyAcknowledged
                ? new GooglePlayBillingServiceAcknowledgePurchaseResult.Success(
                    new AcknowledgePurchaseResponse())
                : acknowledged;
        }

        private static bool IsConnected(GooglePlayBillingServiceStartConnectionResult connected) =>
            connected is GooglePlayBillingServiceStartConnectionResult.Success
            || connected is GooglePlayBillingServiceStartConnectionResult.AlreadyConnected;

        private static GoogleProductDetails Find(QueryProductDetailsResponse response, string productId)
        {
            foreach (var product in response?.ProductDetailsList ?? Array.Empty<GoogleProductDetails>())
            {
                if (product != null && string.Equals(product.ProductId, productId, StringComparison.Ordinal))
                {
                    return product;
                }
            }

            return null;
        }

        // Play's own reason when it gave one: the v9 query answers per product, and an id it could
        // not fetch comes back with a status code rather than an error.
        private static string UnfetchedDetail(QueryProductDetailsResponse response, string productId)
        {
            foreach (var unfetched in response?.UnfetchedProductList ?? Array.Empty<UnfetchedProduct>())
            {
                if (unfetched != null && string.Equals(unfetched.ProductId, productId, StringComparison.Ordinal))
                {
                    return $" (Play status {unfetched.StatusCode})";
                }
            }

            return string.Empty;
        }

        private static string Describe(ProductType productType) =>
            productType == ProductType.Subs ? "a subscription" : "an in-app";

        // The token Play wants on the launch: a subscription's first offer (the base plan), a
        // multi-offer one-time product's first offer, and empty for a simple one-time product —
        // which is what launchBillingFlow expects there.
        // first offer with a token; a game choosing by offerId or tags picks here.
        private static string OfferToken(GoogleProductDetails product)
        {
            foreach (var offer in product.SubscriptionOfferDetails ?? Array.Empty<SubscriptionOfferDetails>())
            {
                if (!string.IsNullOrEmpty(offer?.OfferToken))
                {
                    return offer.OfferToken;
                }
            }

            foreach (var offer in product.OneTimePurchaseOfferDetailsList ?? Array.Empty<OneTimePurchaseOfferDetails>())
            {
                if (!string.IsNullOrEmpty(offer?.OfferToken))
                {
                    return offer.OfferToken;
                }
            }

            return product.OneTimePurchaseOfferDetails?.OfferToken ?? string.Empty;
        }

        // Preserve the original receipt JSON and signature.
        private static string ReceiptJson(GooglePurchase purchase) =>
            "{\"purchase_data\":" + Quote(purchase.OriginalJson)
            + ",\"signature\":" + Quote(purchase.Signature) + "}";

        private static string Quote(string value) =>
            "\"" + (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r") + "\"";

        private static bool Owns(GooglePurchase purchase, string productId)
        {
            if (purchase?.Products == null)
            {
                return false;
            }

            foreach (var product in purchase.Products)
            {
                if (string.Equals(product, productId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // The event carries a raw int rather than a typed Result, so the classification the SDK applies
        // to its request calls is applied here using the same response-code mapping.
        private static StorePurchaseResult FromResponseCode(int code, string debugMessage)
        {
            switch (code)
            {
                case GoogleBillingResponseCode.k_UserCanceled:
                    return StorePurchaseResult.Canceled();

                case GoogleBillingResponseCode.k_ItemAlreadyOwned:
                    // An earlier purchase of this product was never consumed. Retrying the buy cannot
                    // fix it; the open one has to be closed first.
                    return StorePurchaseResult.Business(
                        PurchaseBusinessOutcome.StoreItemAlreadyOwned,
                        "Play reports the item as already owned: an earlier purchase was never "
                        + "consumed.");

                case GoogleBillingResponseCode.k_ServiceUnavailable:
                case GoogleBillingResponseCode.k_BillingUnavailable:
                case GoogleBillingResponseCode.k_NetworkError:
                case GoogleBillingResponseCode.k_ServiceDisconnected:
                    return Failure(HiveErrorCode.Unavailable, code, debugMessage);

                case GoogleBillingResponseCode.k_ItemUnavailable:
                case GoogleBillingResponseCode.k_ItemNotOwned:
                    return Failure(HiveErrorCode.NotFound, code, debugMessage);

                case GoogleBillingResponseCode.k_FeatureNotSupported:
                    return Failure(HiveErrorCode.Unimplemented, code, debugMessage);

                case GoogleBillingResponseCode.k_DeveloperError:
                case GoogleBillingResponseCode.k_Error:
                    return Failure(HiveErrorCode.Internal, code, debugMessage);

                default:
                    return Failure(HiveErrorCode.Unknown, code, debugMessage);
            }
        }

        private static StorePurchaseResult Failure(HiveErrorCode errorCode, int code, string debugMessage)
        {
            var name = GoogleBillingResponseCode.Name(code);
            var detail = string.IsNullOrEmpty(debugMessage) ? string.Empty : $" ({debugMessage})";
            return StorePurchaseResult.Failed(new HiveError(
                errorCode, $"Play answered the purchase with {name}.{detail}", "BillingResponse:" + name));
        }

        private static StorePurchaseResult FromAddonFailure(IAxylResult result, string what)
        {
            var classification = SdkResultClassification.Of(result);
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
                    return StorePurchaseResult.Failed(new HiveError(
                        HiveErrorCode.Unknown, $"Play refused {what}: {result.GetType().Name}."));
            }
        }

        // Preserved the way FromAddonFailure preserves it: an answer nobody recognizes keeps
        // its code and raw response, and everything else keeps the addon's own problem.
        private static StoreOpenPurchasesResult OpenPurchasesRefused(IAxylResult result, string what)
        {
            var classification = SdkResultClassification.Of(result);
            switch (classification.Kind)
            {
                case SdkResultKind.UnknownOutcome:
                    return StoreOpenPurchasesResult.Unrecognized(
                        classification.UnknownCode, classification.RawJson);

                case SdkResultKind.UntypedProblem:
                    return StoreOpenPurchasesResult.Failed(classification.Problem);

                default:
                    return StoreOpenPurchasesResult.Failed(new HiveError(
                        HiveErrorCode.Unknown, $"Play refused {what}: {result.GetType().Name}."));
            }
        }
    }
}

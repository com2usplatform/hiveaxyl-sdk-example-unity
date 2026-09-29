// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Payments;
using Hive.Axyl.Payments.Addon.Apple;
using ApplePurchaseRequest = Hive.Axyl.Payments.Addon.Apple.PurchaseRequest;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Apple's half of a purchase — consumable or subscription. StoreKit takes the payment, and the
    /// transaction has to be finished once the item or the entitlement has been granted.
    /// </summary>
    /// <remarks>
    /// Requires the Apple payments addon on iOS or macOS.
    /// A store payment that is still pending must not grant items. Preserve unverified transactions and their
    /// <see cref="StorePurchaseResult.StoreVerificationError"/> for game-server verification.
    /// After verified delivery, close the transaction to prevent repeated recovery.
    /// </remarks>
    public sealed class ApplePurchaseSource : IStorePurchaseSource, ISubscriptionSource
    {
        private readonly int m_quantity;

        /// <summary>
        /// The purchase options StoreKit is given, or null when neither an account token nor a quantity
        /// above one has anything to add.
        /// </summary>
        private static PurchaseOptions? BuildOptions(string accountUuid, int quantity)
        {
            var hasToken = !string.IsNullOrEmpty(accountUuid);
            if (!hasToken && quantity <= 1)
            {
                return null;
            }

            return new PurchaseOptions
            {
                AppAccountToken = hasToken ? accountUuid : null,
                Quantity = quantity > 1 ? quantity : (int?)null,
            };
        }

        /// <summary>Creates the source for one market.</summary>
        /// <param name="quantity">
        /// Quantity for consumables, subject to the product's App Store limit.
        /// Leave at 1 for subscriptions and non-consumables.
        /// </param>
        public ApplePurchaseSource(int quantity = 1)
        {
            m_quantity = quantity;
        }

        /// <inheritdoc />
        public PurchaseMarket Market => PurchaseMarket.Apple;

        /// <inheritdoc />
        /// <remarks>
        /// StoreKit takes the payment on its own and can deliver the transaction after the app has gone,
        /// so the record written first is what makes that purchase recognizable later.
        /// </remarks>
        public bool RecordsIntentFirst => true;

        /// <inheritdoc />
        /// <remarks>StoreKit resolves the purchase call with the transaction and its JWS.</remarks>
        public bool RecoversPendingFromServer => false;

        /// <inheritdoc />
        public async Task<StorePurchaseResult> PurchaseAsync(
            PurchaseOrder order, CancellationToken cancellationToken)
        {
            if (m_quantity < 1)
            {
                // Refused here rather than at StoreKit, which reports a generic purchase failure that
                // does not name the option.
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.InvalidArgument,
                    $"An Apple order must be for at least one item, not {m_quantity}."));
            }

            if (!HiveCore.TryResolve<IAppleStoreKitPlugin>(out var storeKit) || storeKit == null)
            {
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The Apple StoreKit plugin is not registered. It resolves on iOS and macOS only."));
            }

            var result = await storeKit.PurchaseAsync(
                new ApplePurchaseRequest
                {
                    ProductId = order.ProductId,

                    // Pass the account UUID when supplied. Quantity is included only when greater than one.
                    Options = BuildOptions(order.AccountUuid, m_quantity),
                },
                cancellationToken);

            switch (result)
            {
                case AppleStoreKitServicePurchaseResult.UserCanceled:
                    return StorePurchaseResult.Canceled();

                case AppleStoreKitServicePurchaseResult.Pending:
                    // Ask to Buy: the money has not moved and may never. Reported as a conflict rather
                    // than a failure, because nothing went wrong and a retry is not the answer.
                    return StorePurchaseResult.Business(
                        PurchaseBusinessOutcome.StorePurchasePending,
                        "StoreKit reports the purchase as pending approval. Nothing is owed yet.");

                case AppleStoreKitServicePurchaseResult.Success ok:
                    return FromTransaction(ok.Data?.Transaction);

                default:
                    return FromFailure(result);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Confirms the purchase before finishing the store transaction.
        /// If confirmation fails, retain the purchase and retry closing without granting again.
        /// </remarks>
        public async Task<IAxylResult> FinishAsync(
            PendingPurchase pending, string finalizeReceipt, CancellationToken cancellationToken)
        {
            // Handle closing here; null would select server-side finalization.
            if (!HiveCore.TryResolve<IAppleStoreKitPlugin>(out var storeKit) || storeKit == null)
            {
                return new AppleStoreKitServiceFinishTransactionResult.Failure(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The Apple StoreKit plugin is not registered, so the transaction was not finished. "
                    + "StoreKit will keep redelivering it."));
            }

            if (!ulong.TryParse(
                    pending.FinishToken, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                return new AppleStoreKitServiceFinishTransactionResult.Failure(new HiveError(
                    HiveErrorCode.InvalidArgument,
                    "The purchase carries no StoreKit transaction id, so there is nothing to finish. "
                    + "It was not started by this source."));
            }

            if (!HiveCore.TryResolve<IPaymentsService>(out var payments))
            {
                return new AppleStoreKitServiceFinishTransactionResult.Failure(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The payments Capability is not registered, so the purchase was not confirmed "
                    + "and the transaction was not finished."));
            }

            // The confirm's storeTransactionId is the transaction id the server re-verifies by — the
            // same value the finish token carries for Apple, so a pending that named only one still
            // confirms.
            var confirm = await payments.RequestPurchaseAsync(
                new PurchasePostRequest
                {
                    ProviderId = PurchasePostRequestProviderId.Apple,
                    AxylReceipt = pending.AxylReceipt,
                    StoreTransactionId = string.IsNullOrEmpty(pending.StoreTransactionId)
                        ? pending.FinishToken
                        : pending.StoreTransactionId,
                },
                new ApiCallContext { Token = cancellationToken });

            var classification = SdkResultClassification.Of(confirm);
            if (classification.Kind != SdkResultKind.Success
                && !(confirm is PaymentsRequestPurchaseResult.VerifyDuplicated))
            {
                // Reported instead of finishing anyway: the transaction stays unfinished, StoreKit
                // redelivers it, and the recovery Recipe can run this close again.
                return confirm;
            }

            return await storeKit.FinishTransactionAsync(
                new FinishTransactionRequest { TransactionId = id }, cancellationToken);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Finishes the subscription store transaction after the Recipe confirms it.
        /// </remarks>
        public async Task<IAxylResult> FinishAtStoreAsync(
            PendingPurchase pending, CancellationToken cancellationToken)
        {
            if (!HiveCore.TryResolve<IAppleStoreKitPlugin>(out var storeKit) || storeKit == null)
            {
                return new AppleStoreKitServiceFinishTransactionResult.Failure(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The Apple StoreKit plugin is not registered, so the transaction was not finished. "
                    + "StoreKit will keep redelivering it."));
            }

            if (!ulong.TryParse(
                    pending.FinishToken, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                return new AppleStoreKitServiceFinishTransactionResult.Failure(new HiveError(
                    HiveErrorCode.InvalidArgument,
                    "The subscription carries no StoreKit transaction id, so there is nothing to "
                    + "finish. It was not started by this source."));
            }

            return await storeKit.FinishTransactionAsync(
                new FinishTransactionRequest { TransactionId = id }, cancellationToken);
        }

        /// <inheritdoc />
        /// <remarks>
        /// StoreKit keeps the list: <c>Transaction.unfinished</c> is every purchase that was paid for
        /// and never finished, which for a consumable is exactly the undelivered set. The entries
        /// carry no price — a StoreKit transaction does not name what was paid — so the caller
        /// supplies one where the verify call needs it, from the catalog the player bought against.
        /// </remarks>
        public async Task<StoreOpenPurchasesResult> FindOpenPurchasesAsync(
            CancellationToken cancellationToken)
        {
            if (!HiveCore.TryResolve<IAppleStoreKitPlugin>(out var storeKit) || storeKit == null)
            {
                return StoreOpenPurchasesResult.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The Apple StoreKit plugin is not registered. It resolves on iOS and macOS only."));
            }

            var result = await storeKit.GetUnfinishedTransactionsAsync(
                new GetUnfinishedTransactionsRequest(), cancellationToken);

            var classification = SdkResultClassification.Of(result);
            if (classification.Kind == SdkResultKind.UnknownOutcome)
            {
                // Preserved the way FromFailure preserves it: the code and the raw response
                // are the diagnosis, and flattening them into a message string would lose both.
                return StoreOpenPurchasesResult.Unrecognized(
                    classification.UnknownCode, classification.RawJson);
            }

            if (classification.Kind != SdkResultKind.Success)
            {
                return StoreOpenPurchasesResult.Failed(
                    classification.Problem ?? new HiveError(
                        HiveErrorCode.Unknown,
                        "StoreKit did not answer the unfinished-transactions call."));
            }

            var transactions = (result as AppleStoreKitServiceGetUnfinishedTransactionsResult.Success)
                ?.Data?.Transactions;
            var open = new List<StoreOpenPurchase>();
            if (transactions != null)
            {
                foreach (var transaction in transactions)
                {
                    if (transaction == null)
                    {
                        continue;
                    }

                    // Mapped the way a fresh purchase is mapped, verification objection included —
                    // an entry StoreKit could not verify still lists, and the server's verify is the
                    // authority on it.
                    var id = transaction.Id.ToString(CultureInfo.InvariantCulture);
                    open.Add(new StoreOpenPurchase(
                        transaction.ProductId,
                        transaction.JwsRepresentation,
                        verifyToken: transaction.JwsRepresentation,
                        finishToken: id,
                        storeTransactionId: id,
                        storeVerificationError: VerificationObjection(transaction)));
                }
            }

            return StoreOpenPurchasesResult.Succeeded(open);
        }

        private static StorePurchaseResult FromTransaction(AppleTransaction transaction)
        {
            if (transaction == null)
            {
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.Internal,
                    "StoreKit reported a successful purchase with no transaction on it."));
            }

            if (string.IsNullOrEmpty(transaction.JwsRepresentation))
            {
                // Both server calls take the JWS, so without one the purchase cannot be recorded or
                // verified — and the transaction is still open. Reported rather than half-carried.
                return StorePurchaseResult.Failed(new HiveError(
                    HiveErrorCode.Internal,
                    "StoreKit returned a transaction with no JWS, which both server calls need."));
            }

            // Preserve the transaction ID as both the store transaction ID and finish token.
            var id = transaction.Id.ToString(CultureInfo.InvariantCulture);
            return StorePurchaseResult.Ready(
                transaction.JwsRepresentation,
                verifyToken: transaction.JwsRepresentation,
                finishToken: id,
                storeTransactionId: id,
                storeVerificationError: VerificationObjection(transaction));
        }

        // Empty when StoreKit verified the transaction. Otherwise the reason it could not, which is a
        // separate field on the transaction and is meaningful only in the unverified branch.
        private static string VerificationObjection(AppleTransaction transaction) =>
            transaction.VerificationStatus == AppleVerificationStatus.Verified
                ? string.Empty
                : $"{transaction.VerificationStatus}: {transaction.VerificationError}";

        private static StorePurchaseResult FromFailure(IAxylResult result)
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
                        HiveErrorCode.Unknown,
                        $"StoreKit refused the purchase: {result.GetType().Name}."));
            }
        }
    }
}

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;
using Hive.Axyl.Payments;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// The one place a payment endpoint's typed Outcome becomes the Recipes' vocabulary.
    /// </summary>
    public static class PurchaseOutcomeMap
    {
        /// <summary>
        /// Maps the typed Outcome variants of the client calls a purchase or a subscription can make.
        /// A variant added later lands on <see cref="PurchaseBusinessOutcome.Unrecognized"/> rather
        /// than a nearby meaning.
        /// </summary>
        public static PurchaseBusinessOutcome Of(IAxylResult result)
        {
            switch (result)
            {
                // Every purchase call can be refused as malformed or as naming a bad field. These two
                // are the whole shared set — everything below is the subset a given endpoint can add.
                case PaymentsCreatePrePurchaseResult.PaymentBadRequest:
                case PaymentsCreatePaymentUrlResult.PaymentBadRequest:
                case PaymentsInitiatePurchaseResult.PaymentBadRequest:
                case PaymentsRecordStorePurchaseResult.PaymentBadRequest:
                case PaymentsFinalizePurchaseResult.PaymentBadRequest:
                case PaymentsRestorePurchasesResult.PaymentBadRequest:
                case PaymentsRequestPurchaseResult.PaymentBadRequest:
                case PaymentsPrepareSubscriptionResult.PaymentBadRequest:
                case PaymentsPurchaseSubscriptionResult.PaymentBadRequest:
                case PaymentsPostSubscriptionResult.PaymentBadRequest:
                    return PurchaseBusinessOutcome.PaymentBadRequest;

                case PaymentsCreatePrePurchaseResult.PaymentInvalidParameter:
                case PaymentsCreatePaymentUrlResult.PaymentInvalidParameter:
                case PaymentsInitiatePurchaseResult.PaymentInvalidParameter:
                case PaymentsRecordStorePurchaseResult.PaymentInvalidParameter:
                case PaymentsFinalizePurchaseResult.PaymentInvalidParameter:
                case PaymentsRestorePurchasesResult.PaymentInvalidParameter:
                case PaymentsRequestPurchaseResult.PaymentInvalidParameter:
                case PaymentsPrepareSubscriptionResult.PaymentInvalidParameter:
                case PaymentsPurchaseSubscriptionResult.PaymentInvalidParameter:
                case PaymentsPostSubscriptionResult.PaymentInvalidParameter:
                    return PurchaseBusinessOutcome.PaymentInvalidParameter;

                // Only the calls that look something up can fail to find it.
                case PaymentsRecordStorePurchaseResult.PaymentResourceNotFound:
                case PaymentsFinalizePurchaseResult.PaymentResourceNotFound:
                case PaymentsRestorePurchasesResult.PaymentResourceNotFound:
                case PaymentsRequestPurchaseResult.PaymentResourceNotFound:
                case PaymentsPrepareSubscriptionResult.PaymentResourceNotFound:
                case PaymentsPurchaseSubscriptionResult.PaymentResourceNotFound:
                case PaymentsPostSubscriptionResult.PaymentResourceNotFound:
                    return PurchaseBusinessOutcome.PaymentResourceNotFound;

                case PaymentsPurchaseSubscriptionResult.PaymentResourceConflict:
                    return PurchaseBusinessOutcome.PaymentResourceConflict;

                // Only the closing calls check the session against the purchase.
                case PaymentsRecordStorePurchaseResult.PaymentUnauthorized:
                case PaymentsRequestPurchaseResult.PaymentUnauthorized:
                case PaymentsPurchaseSubscriptionResult.PaymentUnauthorized:
                case PaymentsPostSubscriptionResult.PaymentUnauthorized:
                    return PurchaseBusinessOutcome.PaymentUnauthorized;

                // Already-confirmed results are not mapped here; the closing flow handles them.
                case PaymentsRecordStorePurchaseResult.VerifyError:
                case PaymentsRequestPurchaseResult.VerifyError:
                case PaymentsPurchaseSubscriptionResult.VerifyError:
                case PaymentsPostSubscriptionResult.VerifyError:
                    return PurchaseBusinessOutcome.VerifyError;

                default:
                    return PurchaseBusinessOutcome.Unrecognized;
            }
        }
    }
}

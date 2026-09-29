// Copyright (c) Com2uS Platform Corp. All rights reserved.

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Google Play Billing's <c>BillingResponseCode</c> values, as numbers.
    /// </summary>
    /// <remarks>
    /// Values match Google Play Billing response codes. Compare the numeric values, not display names.
    /// </remarks>
    internal static class GoogleBillingResponseCode
    {
        /// <summary>The call succeeded.</summary>
        internal const int k_Ok = 0;

        /// <summary>The player closed the purchase sheet. Not a failure.</summary>
        internal const int k_UserCanceled = 1;

        /// <summary>Play's billing service is briefly unreachable.</summary>
        internal const int k_ServiceUnavailable = 2;

        /// <summary>Billing is unavailable on this device — no Play Store, or it is blocked.</summary>
        internal const int k_BillingUnavailable = 3;

        /// <summary>The product is not available for purchase.</summary>
        internal const int k_ItemUnavailable = 4;

        /// <summary>The request was malformed. An app-side mistake, not the player's.</summary>
        internal const int k_DeveloperError = 5;

        /// <summary>Play reported a generic error.</summary>
        internal const int k_Error = 6;

        /// <summary>The player already owns the product — an earlier purchase was never consumed.</summary>
        internal const int k_ItemAlreadyOwned = 7;

        /// <summary>The player does not own the product being consumed or acknowledged.</summary>
        internal const int k_ItemNotOwned = 8;

        /// <summary>The device could not reach Play.</summary>
        internal const int k_NetworkError = 12;

        /// <summary>The billing client lost its connection.</summary>
        internal const int k_ServiceDisconnected = -1;

        /// <summary>The requested feature is not supported on this device or Play version.</summary>
        internal const int k_FeatureNotSupported = -2;

        /// <summary>
        /// The code's name, for a message a person has to read. An unrecognized code keeps its number
        /// rather than being reported as something it is not.
        /// </summary>
        internal static string Name(int code)
        {
            switch (code)
            {
                case k_Ok: return "OK";
                case k_UserCanceled: return "USER_CANCELED";
                case k_ServiceUnavailable: return "SERVICE_UNAVAILABLE";
                case k_BillingUnavailable: return "BILLING_UNAVAILABLE";
                case k_ItemUnavailable: return "ITEM_UNAVAILABLE";
                case k_DeveloperError: return "DEVELOPER_ERROR";
                case k_Error: return "ERROR";
                case k_ItemAlreadyOwned: return "ITEM_ALREADY_OWNED";
                case k_ItemNotOwned: return "ITEM_NOT_OWNED";
                case k_NetworkError: return "NETWORK_ERROR";
                case k_ServiceDisconnected: return "SERVICE_DISCONNECTED";
                case k_FeatureNotSupported: return "FEATURE_NOT_SUPPORTED";
                default: return "UNKNOWN_" + code;
            }
        }
    }
}

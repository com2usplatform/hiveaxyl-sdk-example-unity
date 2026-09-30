// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Payments;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Maps purchase markets to SDK request provider values.
    /// </summary>
    internal static class PurchaseProviders
    {
        internal static PrePurchaseProviderId ToPrePurchase(PurchaseMarket market)
        {
            switch (market)
            {
                case PurchaseMarket.Google: return PrePurchaseProviderId.Google;
                case PurchaseMarket.Apple: return PrePurchaseProviderId.Apple;
                case PurchaseMarket.Pg: return PrePurchaseProviderId.Pg;
                default: return PrePurchaseProviderId.Steam;
            }
        }

        internal static PurchaseRequestProviderId ToPurchase(PurchaseMarket market)
        {
            switch (market)
            {
                case PurchaseMarket.Google: return PurchaseRequestProviderId.Google;
                case PurchaseMarket.Apple: return PurchaseRequestProviderId.Apple;
                case PurchaseMarket.Pg: return PurchaseRequestProviderId.Pg;
                default: return PurchaseRequestProviderId.Steam;
            }
        }

        internal static PurchaseRestoreRequestProviderId ToRestore(PurchaseMarket market)
        {
            switch (market)
            {
                case PurchaseMarket.Google: return PurchaseRestoreRequestProviderId.Google;
                case PurchaseMarket.Apple: return PurchaseRestoreRequestProviderId.Apple;
                case PurchaseMarket.Pg: return PurchaseRestoreRequestProviderId.Pg;
                default: return PurchaseRestoreRequestProviderId.Steam;
            }
        }

        internal static PurchaseFinalizeRequestProviderId ToFinalize(PurchaseMarket market)
        {
            switch (market)
            {
                case PurchaseMarket.Google: return PurchaseFinalizeRequestProviderId.Google;
                case PurchaseMarket.Apple: return PurchaseFinalizeRequestProviderId.Apple;
                case PurchaseMarket.Pg: return PurchaseFinalizeRequestProviderId.Pg;
                default: return PurchaseFinalizeRequestProviderId.Steam;
            }
        }
    }
}

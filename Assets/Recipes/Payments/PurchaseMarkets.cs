// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Collections.Generic;
using System.Linq;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Which markets a purchase can be made in on the platform the app is running on.
    /// </summary>
    /// <remarks>
    /// Use this list to offer only the markets supported by the current build.
    /// </remarks>
    public static class PurchaseMarkets
    {
        /// <summary>
        /// The markets this build can reach, in the order a screen should offer them: the platform's
        /// own store first, since that is the one a player expects, and PG last.
        /// </summary>
        public static IReadOnlyList<PurchaseMarket> Available { get; } = Build();

        /// <summary>Whether this build can reach <paramref name="market"/> at all.</summary>
        public static bool Supports(PurchaseMarket market) => Available.Contains(market);

        private static IReadOnlyList<PurchaseMarket> Build()
        {
            var markets = new List<PurchaseMarket>();

#if UNITY_ANDROID
            markets.Add(PurchaseMarket.Google);
#endif

#if UNITY_IOS || UNITY_STANDALONE_OSX
            markets.Add(PurchaseMarket.Apple);
#endif

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
            markets.Add(PurchaseMarket.Steam);
#endif

            if (PgOsCode.Current != null)
            {
                markets.Add(PurchaseMarket.Pg);
            }

            return markets;
        }
    }
}

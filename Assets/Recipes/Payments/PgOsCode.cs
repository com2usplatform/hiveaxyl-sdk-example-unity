// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Payments;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// The client OS a PG order carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order request's <c>os</c> field is required and typed <see cref="OrderRequestOs"/> —
    /// Windows, Macos, Android, Ios or Webgl. A platform outside that set is marked with
    /// <c>null</c>, never <see cref="OrderRequestOs.Unspecified"/>, which would serialize as a
    /// value the server refuses.
    /// </para>
    /// <para>
    /// <see cref="PurchaseMarkets"/> uses this mapping to offer PG only where an order can name
    /// its OS.
    /// </para>
    /// </remarks>
    internal static class PgOsCode
    {
        /// <summary>
        /// This build's OS value, or <c>null</c> where PG has none — in which case PG is not a
        /// market this build can offer.
        /// </summary>
        internal static OrderRequestOs? Current { get; } = Resolve();

        private static OrderRequestOs? Resolve()
        {
#if UNITY_ANDROID
            return OrderRequestOs.Android;
#elif UNITY_IOS
            return OrderRequestOs.Ios;
#elif UNITY_STANDALONE_WIN
            return OrderRequestOs.Windows;
#elif UNITY_STANDALONE_OSX
            return OrderRequestOs.Macos;
#elif UNITY_WEBGL
            return OrderRequestOs.Webgl;
#else
            // Linux, a console: the enum has no value for it, so PG is not offered.
            return null;
#endif
        }
    }
}

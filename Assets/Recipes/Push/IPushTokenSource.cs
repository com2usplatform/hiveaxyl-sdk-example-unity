// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// The push provider and, for APNs, its environment. Use the environment from the build entitlement.
    /// </summary>
    public enum PushTokenProvider
    {
        /// <summary>Firebase Cloud Messaging — Android.</summary>
        Fcm,

        /// <summary>APNs production — an App Store or TestFlight build.</summary>
        Apns,

        /// <summary>APNs sandbox — a development-signed build.</summary>
        ApnsSandbox,
    }

    /// <summary>
    /// Provides notification-permission and device-token operations.
    /// The supplied APNs and FCM implementations require their corresponding
    /// SDK plugins to be registered on a supported platform.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The application's lifetime owner subscribes to <see cref="TokenRefreshed"/> and
    /// serializes registration. During a call, retain the latest event token and repeat after
    /// success only when it differs from the registered token. Stop on failure or cancellation.
    /// The Recipe does not own the event subscription.
    /// </para>
    /// <para>
    /// Disposing the source releases that subscription. A source outliving its screen would keep
    /// forwarding refreshes into a dead handler.
    /// </para>
    /// </remarks>
    public interface IPushTokenSource : IDisposable
    {
        /// <summary>
        /// Checks or requests notification permission.
        /// Prompt behavior depends on the platform and permission state.
        /// </summary>
        Task<PushAuthorizationResult> RequestAuthorizationAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Issues (or re-reads) the device token, together with the provider it belongs to.
        /// </summary>
        Task<PushTokenResult> GetTokenAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Reports a device token, including callbacks that complete a token lookup.
        /// The token may be unchanged or already registered.
        /// The caller decides whether another registration is needed.
        /// </summary>
        event Action<string> TokenRefreshed;
    }
}

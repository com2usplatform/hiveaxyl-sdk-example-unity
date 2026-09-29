// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Push.Addon.APNS;
#if (UNITY_IOS || UNITY_EDITOR) && HIVE_AXYL_UNITY_MOBILE_NOTIFICATIONS
using Unity.Notifications.iOS;
#endif

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// APNs' half of preparing for push: the OS permission ask, and the device token with the
    /// environment it belongs to.
    /// </summary>
    /// <remarks>
    /// Requires the APNs addon on iOS or macOS. Install the SDK guide's delegate forwarder
    /// before token lookup; otherwise the token callback cannot complete.
    /// On iOS, Unity Mobile Notifications is required. Permission is shared with local notifications.
    /// Restore APNs forwarding in a finally block after calls that may request notification permission,
    /// including failure and cancellation. The build entitlement determines the APNs environment.
    /// </remarks>
    public sealed class ApnsPushTokenSource : IPushTokenSource
    {
        private IAPNSPlugin m_subscribed;
        private Action<string> m_refreshed;

        /// <inheritdoc />
        /// <remarks>
        /// Create and subscribe after SDK initialization. Dispose the source to release its addon subscription.
        /// </remarks>
        public event Action<string> TokenRefreshed
        {
            add
            {
                if (m_subscribed == null
                    && HiveCore.TryResolve<IAPNSPlugin>(out var plugin) && plugin != null)
                {
                    m_subscribed = plugin;
                    m_subscribed.TokenRefreshed += OnTokenRefreshed;
                }

                m_refreshed += value;
            }
            remove => m_refreshed -= value;
        }

        /// <inheritdoc />
        /// <remarks>
        /// The OS dialog is not cancelable, so a canceled token stops the wait rather than the
        /// dialog — the Recipe re-checks it after the await, the way it does for every step.
        /// </remarks>
        public Task<PushAuthorizationResult> RequestAuthorizationAsync(
            CancellationToken cancellationToken)
        {
            if (!HiveCore.TryResolve<IAPNSPlugin>(out var plugin) || plugin == null)
            {
                return Task.FromResult(PushAuthorizationResult.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The APNs plugin is not registered. It resolves on iOS and macOS only.")));
            }

#if UNITY_IOS && !UNITY_EDITOR && HIVE_AXYL_UNITY_MOBILE_NOTIFICATIONS
            return AskOsAsync(cancellationToken);
#elif UNITY_IOS && !UNITY_EDITOR
            // The engine owns the ask on iOS, and the addon asks for nothing, so without the
            // package there is nothing to ask with.
            return Task.FromResult(PushAuthorizationResult.Failed(new HiveError(
                HiveErrorCode.FailedPrecondition,
                "The iOS permission ask needs com.unity.mobile.notifications, which is not installed.")));
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            // Unity Mobile Notifications does not support macOS, so request permission through the addon.
            return AskAddonAsync(plugin, cancellationToken);
#else
            // No native permission prompt runs on this platform. The plugin registration
            // check above determines whether preparation can proceed.
            return Task.FromResult(PushAuthorizationResult.Granted());
#endif
        }

        /// <inheritdoc />
        /// <remarks>
        /// Two reads, and the second is not optional: the token alone does not say which APNs it
        /// belongs to, and the registration needs to know. Requires the forwarder template's
        /// <c>Install</c> to have run — the token resolves through the OS callback it forwards.
        /// </remarks>
        public async Task<PushTokenResult> GetTokenAsync(CancellationToken cancellationToken)
        {
            if (!HiveCore.TryResolve<IAPNSPlugin>(out var plugin) || plugin == null)
            {
                return PushTokenResult.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The APNs plugin is not registered. It resolves on iOS and macOS only."));
            }

            var environment = await plugin.GetProviderEnvironmentAsync(cancellationToken);
            if (!(environment is ApnsServiceGetProviderEnvironmentResult.Success env))
            {
                var envClass = SdkResultClassification.Of(environment);
                return PushTokenResult.Failed(
                    envClass.Problem ?? new HiveError(
                        HiveErrorCode.Unknown,
                        "The build's aps-environment entitlement could not be read: "
                        + $"{envClass.UnknownCode}"));
            }

            var token = await plugin.GetTokenAsync(cancellationToken);
            if (token is ApnsServiceGetTokenResult.Success issued
                && !string.IsNullOrEmpty(issued.Data?.Token))
            {
                return PushTokenResult.Issued(
                    issued.Data.Token,
                    env.Data?.Environment == ProviderEnvironment.ApnsSandbox
                        ? PushTokenProvider.ApnsSandbox
                        : PushTokenProvider.Apns);
            }

            var tokenClass = SdkResultClassification.Of(token);
            return PushTokenResult.Failed(
                tokenClass.Problem ?? new HiveError(
                    HiveErrorCode.Unknown,
                    $"APNs issued no device token: {tokenClass.UnknownCode}"));
        }

        /// <summary>Releases the addon-event subscription this source took.</summary>
        public void Dispose()
        {
            if (m_subscribed != null)
            {
                m_subscribed.TokenRefreshed -= OnTokenRefreshed;
                m_subscribed = null;
            }

            m_refreshed = null;
        }

        private void OnTokenRefreshed(string token) => m_refreshed?.Invoke(token);

#if (UNITY_IOS || UNITY_EDITOR) && HIVE_AXYL_UNITY_MOBILE_NOTIFICATIONS
        // What the iOS settings already say, or null when the OS has not been asked yet. Paired with
        // UnityLocalNotificationSource.OnFile in LocalNotification/ — the two Recipes share one
        // OS answer and must read it the same way.
        internal static PushAuthorizationResult OnFile(AuthorizationStatus status)
        {
            switch (status)
            {
                case AuthorizationStatus.Authorized:
                case AuthorizationStatus.Provisional:
                case AuthorizationStatus.Ephemeral:
                    return PushAuthorizationResult.Granted();

                case AuthorizationStatus.Denied:
                    return PushAuthorizationResult.Denied();

                default:
                    return null;
            }
        }

        // The prompt's answer: a framework fault is a failure with its message, and otherwise the
        // raw flag is the player's — false is a refusal, not an error. Paired with
        // UnityLocalNotificationSource.FromAsk in LocalNotification/.
        internal static PushAuthorizationResult FromAsk(bool granted, string error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                return PushAuthorizationResult.Failed(new HiveError(HiveErrorCode.Internal, error));
            }

            return granted ? PushAuthorizationResult.Granted() : PushAuthorizationResult.Denied();
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR && HIVE_AXYL_UNITY_MOBILE_NOTIFICATIONS
        private static async Task<PushAuthorizationResult> AskOsAsync(
            CancellationToken cancellationToken)
        {
            // Read before asking, and never ask once the answer is on file: every ask re-points
            // the notification-center delegate to Unity's, and the fewer of those the fewer
            // re-installs the app owes the forwarder template.
            var onFile = OnFile(iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus);
            if (onFile != null)
            {
                return onFile;
            }

            // registerForRemoteNotifications stays false: the APNs registration and the token
            // path are the addon's, through the forwarder template's app-delegate hooks.
            using (var request = new AuthorizationRequest(
                AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound,
                registerForRemoteNotifications: false))
            {
                while (!request.IsFinished)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return PushAuthorizationResult.Failed(new HiveError(
                            HiveErrorCode.Cancelled, "The caller canceled while the ask was open."));
                    }

                    await Task.Yield();
                }

                return FromAsk(request.Granted, request.Error);
            }
        }
#endif

        // Translate the macOS authorization result. A false grant flag means permission
        // was not granted; framework errors are reported as Failure.
        internal static PushAuthorizationResult FromAddon(ApnsServiceRequestAuthorizationResult asked)
        {
            if (asked is ApnsServiceRequestAuthorizationResult.Success granted)
            {
                return granted.Data?.Granted == true
                    ? PushAuthorizationResult.Granted()
                    : PushAuthorizationResult.Denied();
            }

            var classification = SdkResultClassification.Of(asked);
            return PushAuthorizationResult.Failed(
                classification.Problem ?? new HiveError(
                    HiveErrorCode.Unknown,
                    $"The OS did not answer the permission ask: {classification.UnknownCode}"));
        }

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        private static async Task<PushAuthorizationResult> AskAddonAsync(
            IAPNSPlugin plugin, CancellationToken cancellationToken) =>
            FromAddon(await plugin.RequestAuthorizationAsync(
                new[]
                {
                    UNAuthorizationOption.Alert,
                    UNAuthorizationOption.Badge,
                    UNAuthorizationOption.Sound,
                },
                cancellationToken));
#endif
    }
}

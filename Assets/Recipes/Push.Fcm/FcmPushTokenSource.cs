// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Push.Addon.FCM;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
using UnityEngine.Android;
#endif

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// FCM's half of preparing for push: the OS permission ask, and the registration token.
    /// </summary>
    /// <remarks>
    /// Requires the FCM addon on Android and <c>google-services.json</c> at
    /// <c>Assets/Plugins/Android/</c>. Notification permission is requested on Android 13 and later.
    /// Permission denial prevents registration by the Recipe.
    /// </remarks>
    public sealed class FcmPushTokenSource : IPushTokenSource
    {
        private IFCMPlugin m_subscribed;
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
                    && HiveCore.TryResolve<IFCMPlugin>(out var plugin) && plugin != null)
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
        /// Caller cancellation does not close the OS dialog. This source waits for the permission
        /// callback; the Recipe checks cancellation after the await.
        /// </remarks>
        public Task<PushAuthorizationResult> RequestAuthorizationAsync(
            CancellationToken cancellationToken)
        {
            if (!HiveCore.TryResolve<IFCMPlugin>(out var plugin) || plugin == null)
            {
                return Task.FromResult(PushAuthorizationResult.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The FCM plugin is not registered. It resolves on Android only.")));
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            return AskOsAsync();
#else
            // No native permission prompt runs on this platform. The plugin registration
            // check above determines whether preparation can proceed.
            return Task.FromResult(PushAuthorizationResult.Granted());
#endif
        }

        /// <inheritdoc />
        /// <remarks>
        /// Requires Google Play services. Unavailable services are reported through the result.
        /// </remarks>
        public async Task<PushTokenResult> GetTokenAsync(CancellationToken cancellationToken)
        {
            if (!HiveCore.TryResolve<IFCMPlugin>(out var plugin) || plugin == null)
            {
                return PushTokenResult.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "The FCM plugin is not registered. It resolves on Android only."));
            }

            var token = await plugin.GetTokenAsync(cancellationToken);
            if (token is FcmServiceGetTokenResult.Success issued
                && !string.IsNullOrEmpty(issued.Data?.Token))
            {
                return PushTokenResult.Issued(issued.Data.Token, PushTokenProvider.Fcm);
            }

            var classification = SdkResultClassification.Of(token);
            return PushTokenResult.Failed(
                classification.Problem ?? new HiveError(
                    HiveErrorCode.Unknown,
                    $"FCM issued no registration token: {classification.UnknownCode}"));
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

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string k_PostNotifications = "android.permission.POST_NOTIFICATIONS";

        private static Task<PushAuthorizationResult> AskOsAsync()
        {
            // Android 12 and below have no POST_NOTIFICATIONS — notifications are allowed without
            // an ask, and asking for the unknown permission would answer denied.
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                if (version.GetStatic<int>("SDK_INT") < 33)
                {
                    return Task.FromResult(PushAuthorizationResult.Granted());
                }
            }

            if (Permission.HasUserAuthorizedPermission(k_PostNotifications))
            {
                return Task.FromResult(PushAuthorizationResult.Granted());
            }

            // Android decides whether another prompt can be shown for the current permission state.
            var asked = new TaskCompletionSource<PushAuthorizationResult>();
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => asked.TrySetResult(PushAuthorizationResult.Granted());
            callbacks.PermissionDenied += _ => asked.TrySetResult(PushAuthorizationResult.Denied());
            Permission.RequestUserPermission(k_PostNotifications, callbacks);
            return asked.Task;
        }
#endif
    }
}

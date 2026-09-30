// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
using Unity.Notifications;
#endif
#if UNITY_IOS || UNITY_EDITOR
using Unity.Notifications.iOS;
#endif

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Unity Mobile Notifications as the platform's half: the OS permission ask and the OS
    /// scheduler, through the package's unified API.
    /// </summary>
    /// <remarks>
    /// Supported on Android and iOS players; other platforms return <c>Unimplemented</c>.
    /// Create one source at startup with the desired Android channel; later initialization
    /// does not replace the first channel configuration.
    /// On iOS, restore APNs forwarding in a finally block after calls that may request permission,
    /// including failure and cancellation.
    /// </remarks>
    public sealed class UnityLocalNotificationSource : ILocalNotificationSource
    {
        private const string k_NoNotificationCenter =
            "Local notifications need an Android or iOS player; the Editor and the desktop players "
            + "have no notification center.";

        /// <summary>
        /// Creates the source and, on Android, registers the channel notifications go out on.
        /// </summary>
        /// <param name="androidChannelId">The channel's id. Must not be blank.</param>
        /// <param name="androidChannelName">
        /// The name the OS settings screen shows for the channel. Must not be blank.
        /// </param>
        /// <param name="androidChannelDescription">What the settings screen says under the name.</param>
        public UnityLocalNotificationSource(
            string androidChannelId, string androidChannelName, string androidChannelDescription = null)
        {
            if (string.IsNullOrWhiteSpace(androidChannelId))
            {
                throw new ArgumentException("A channel id is required.", nameof(androidChannelId));
            }

            if (string.IsNullOrWhiteSpace(androidChannelName))
            {
                throw new ArgumentException("A channel name is required.", nameof(androidChannelName));
            }

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            // Alert, badge, sound: what the permission ask names on iOS, and on Android what makes
            // the channel high-importance — a heads-up banner rather than a silent tray entry.
            NotificationCenter.Initialize(new NotificationCenterArgs
            {
                PresentationOptions = NotificationPresentation.Alert
                    | NotificationPresentation.Badge
                    | NotificationPresentation.Sound,
                AndroidChannelId = androidChannelId,
                AndroidChannelName = androidChannelName,
                AndroidChannelDescription = androidChannelDescription,
            });
#endif
        }

        /// <inheritdoc />
        public Task<LocalNotificationPermissionResult> RequestPermissionAsync(
            CancellationToken cancellationToken)
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            return AskOsAsync(cancellationToken);
#else
            return Task.FromResult(LocalNotificationPermissionResult.Failed(
                new HiveError(HiveErrorCode.Unimplemented, k_NoNotificationCenter)));
#endif
        }

        /// <inheritdoc />
        public LocalNotificationScheduleResult Schedule(LocalNotificationRequest request)
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            try
            {
                // The id is the OS's to assign: Unity's explicit-id path on Android cannot report
                // a notification manager that is not up, the auto-id path answers -1 for it.
                var notification = new Notification
                {
                    Title = request.Title,
                    Text = request.Body,
                    ShowInForeground = true,
                };

                // An interval rather than a date: the package's date schedule reads the date as
                // device-local on Android and honors UTC on iOS, and an interval sidesteps both.
                var now = request.FireAt.Kind == DateTimeKind.Utc ? DateTime.UtcNow : DateTime.Now;
                var delay = request.FireAt - now;

                // a fire time that slipped into the past while the permission dialog was
                // open fires now instead — iOS refuses a non-positive interval outright.
                if (delay < TimeSpan.FromSeconds(1))
                {
                    delay = TimeSpan.FromSeconds(1);
                }

                var id = NotificationCenter.ScheduleNotification(
                    notification, new NotificationIntervalSchedule(delay));

                // Android answers -1 when its notification manager did not come up.
                return id < 0
                    ? LocalNotificationScheduleResult.Failed(new HiveError(
                        HiveErrorCode.Unavailable, "The OS notification manager is not available."))
                    : LocalNotificationScheduleResult.Scheduled(id);
            }
            catch (Exception e)
            {
                return LocalNotificationScheduleResult.Failed(
                    new HiveError(HiveErrorCode.Internal, e.Message));
            }
#else
            return LocalNotificationScheduleResult.Failed(
                new HiveError(HiveErrorCode.Unimplemented, k_NoNotificationCenter));
#endif
        }

        /// <inheritdoc />
        public HiveError Cancel(int id)
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            try
            {
                NotificationCenter.CancelScheduledNotification(id);
                return null;
            }
            catch (Exception e)
            {
                return new HiveError(HiveErrorCode.Internal, e.Message);
            }
#else
            return new HiveError(HiveErrorCode.Unimplemented, k_NoNotificationCenter);
#endif
        }

        /// <inheritdoc />
        public HiveError CancelAll()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            try
            {
                NotificationCenter.CancelAllScheduledNotifications();
                return null;
            }
            catch (Exception e)
            {
                return new HiveError(HiveErrorCode.Internal, e.Message);
            }
#else
            return new HiveError(HiveErrorCode.Unimplemented, k_NoNotificationCenter);
#endif
        }

#if UNITY_IOS || UNITY_EDITOR
        // What the iOS settings already say, or null when the OS has not been asked yet. Paired with
        // ApnsPushTokenSource.OnFile in Push.Apns/ — the two Recipes share one OS answer and must
        // read it the same way.
        internal static LocalNotificationPermissionResult OnFile(AuthorizationStatus status)
        {
            switch (status)
            {
                case AuthorizationStatus.Authorized:
                case AuthorizationStatus.Provisional:
                case AuthorizationStatus.Ephemeral:
                    return LocalNotificationPermissionResult.Granted();

                case AuthorizationStatus.Denied:
                    return LocalNotificationPermissionResult.Denied();

                default:
                    return null;
            }
        }
#endif

#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
        // The completed unified request reports Granted or Denied, without a separate
        // dismissal result. A non-granted result does not identify the user's action.
        internal static LocalNotificationPermissionResult FromAsk(NotificationsPermissionStatus status) =>
            status == NotificationsPermissionStatus.Granted
                ? LocalNotificationPermissionResult.Granted()
                : LocalNotificationPermissionResult.Denied();
#endif

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        private static async Task<LocalNotificationPermissionResult> AskOsAsync(
            CancellationToken cancellationToken)
        {
            try
            {
#if UNITY_IOS
                // Read before asking, and never ask once the answer is on file — see the class
                // remarks for what an avoidable ask costs on iOS. Android's request does this on
                // its own.
                var onFile = OnFile(iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus);
                if (onFile != null)
                {
                    return onFile;
                }
#endif

                var request = NotificationCenter.RequestPermission();
                while (request.keepWaiting)
                {
                    // The OS dialog cannot be withdrawn; the caller just stops waiting for it.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return LocalNotificationPermissionResult.Failed(new HiveError(
                            HiveErrorCode.Cancelled, "The caller canceled while the ask was open."));
                    }

                    await Task.Yield();
                }

                return FromAsk(request.Status);
            }
            catch (Exception e)
            {
                return LocalNotificationPermissionResult.Failed(
                    new HiveError(HiveErrorCode.Internal, e.Message));
            }
        }
#endif
    }
}

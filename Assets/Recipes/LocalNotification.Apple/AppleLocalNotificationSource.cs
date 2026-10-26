// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Push.Addon.AppleNotification;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// The SDK's Apple local-notification add-on as the platform's half: the OS permission ask
    /// and the OS scheduler, through <see cref="ILocalNotificationApplePlugin"/>. macOS only —
    /// iOS local notifications are Unity Mobile Notifications', see
    /// <see cref="UnityLocalNotificationSource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The add-on schedules under a string id and the Recipe hands back an int; this source
    /// draws a random id per request and schedules it as <c>recipe-N</c>, so
    /// <c>CancelAllAsync</c> withdraws what this source scheduled and leaves the game's other
    /// notifications alone. The draw, rather than a counter, because Apple replaces a pending
    /// notification scheduled under an id it has seen: a counter restarting at zero — a new
    /// source, or a new app run — would silently swallow earlier schedules.
    /// </para>
    /// <para>
    /// Delivery is not the source's to see: a foreground presentation and the tap that opened
    /// the app come out of the add-on's events, which the delegate forwarder in this folder
    /// (<c>LocalNotificationAppleForwarder.Install</c>) raises. Install it once at startup;
    /// without it the events never fire, while scheduling still works.
    /// </para>
    /// </remarks>
    public sealed class AppleLocalNotificationSource : ILocalNotificationSource
    {
        // What the permission ask names, the same three Unity Mobile Notifications asks for.
        private static readonly UNLocalAuthorizationOption[] s_options =
        {
            UNLocalAuthorizationOption.Alert,
            UNLocalAuthorizationOption.Badge,
            UNLocalAuthorizationOption.Sound,
        };

        private const string k_IdPrefix = "recipe-";

        private readonly ILocalNotificationApplePlugin m_plugin;

        /// <param name="plugin">The add-on, resolved from the registry after initialization.</param>
        public AppleLocalNotificationSource(ILocalNotificationApplePlugin plugin)
        {
            m_plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        }

        /// <inheritdoc />
        public async Task<LocalNotificationPermissionResult> RequestPermissionAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return FromAddon(await m_plugin.RequestPermissionAsync(s_options, cancellationToken));
            }
            catch (Exception e)
            {
                return LocalNotificationPermissionResult.Failed(
                    new HiveError(HiveErrorCode.Internal, e.Message));
            }
        }

        /// <inheritdoc />
        public async Task<LocalNotificationScheduleResult> ScheduleAsync(
            LocalNotificationRequest request, CancellationToken cancellationToken)
        {
            // An interval rather than a date, for the same reason the Unity source uses one: the
            // request's fire time is a point on whichever clock it was written against.
            var now = request.FireAt.Kind == DateTimeKind.Utc ? DateTime.UtcNow : DateTime.Now;
            var delay = request.FireAt - now;
            if (delay < TimeSpan.FromSeconds(1))
            {
                delay = TimeSpan.FromSeconds(1);
            }

            var id = NextId();
            var content = new NotificationContent { Title = request.Title, Body = request.Body };
            try
            {
                var scheduled = await m_plugin.ScheduleTimeIntervalAsync(
                    IdOf(id), content, delay, repeats: false, cancellationToken);
                if (scheduled is LocalNotificationAppleServiceScheduleTimeIntervalResult.Success)
                {
                    return LocalNotificationScheduleResult.Scheduled(id);
                }

                var classification = SdkResultClassification.Of(scheduled);
                return LocalNotificationScheduleResult.Failed(
                    classification.Problem ?? new HiveError(
                        HiveErrorCode.Unknown,
                        $"The OS took no notification: {classification.UnknownCode}"));
            }
            catch (OperationCanceledException)
            {
                // The cancel stopped the bridge wait, not the OS, which may already hold the
                // reservation — whether the call reached the OS at all is unknowable from here,
                // and withdrawing an id the OS never took is a no-op, so the withdraw is tried
                // either way. Should even the withdraw fail, the reservation may stand — then
                // the id must go out, as scheduled, rather than leave a notification nothing
                // can name.
                var withdrawn = await CancelAsync(id, CancellationToken.None);
                return withdrawn == null
                    ? LocalNotificationScheduleResult.Failed(new HiveError(
                        HiveErrorCode.Cancelled, "The caller canceled while scheduling."))
                    : LocalNotificationScheduleResult.Scheduled(id);
            }
            catch (Exception e)
            {
                return LocalNotificationScheduleResult.Failed(
                    new HiveError(HiveErrorCode.Internal, e.Message));
            }
        }

        /// <inheritdoc />
        public async Task<HiveError> CancelAsync(int id, CancellationToken cancellationToken)
        {
            try
            {
                return Problem(await m_plugin.CancelPendingAsync(IdOf(id), cancellationToken));
            }
            catch (Exception e)
            {
                return new HiveError(HiveErrorCode.Internal, e.Message);
            }
        }

        /// <inheritdoc />
        public async Task<HiveError> CancelAllAsync(CancellationToken cancellationToken)
        {
            // The add-on cancels one id at a time, so "all" is every pending id this source
            // scheduled — the game's own notifications are not this Recipe's to withdraw.
            try
            {
                var listed = await m_plugin.ListPendingAsync(cancellationToken);
                if (!(listed is LocalNotificationAppleServiceListPendingResult.Success pending))
                {
                    return Problem(listed);
                }

                foreach (var request in pending.Data?.Requests ?? Array.Empty<UNNotificationRequestSummary>())
                {
                    if (!request.NotificationId.StartsWith(k_IdPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var error = Problem(await m_plugin.CancelPendingAsync(
                        request.NotificationId, cancellationToken));
                    if (error != null)
                    {
                        return error;
                    }
                }

                return null;
            }
            catch (Exception e)
            {
                return new HiveError(HiveErrorCode.Internal, e.Message);
            }
        }

        // Translate the authorization result. A false grant flag means permission was not
        // granted; framework errors are reported as Failure.
        internal static LocalNotificationPermissionResult FromAddon(
            LocalNotificationAppleServiceRequestPermissionResult asked)
        {
            if (asked is LocalNotificationAppleServiceRequestPermissionResult.Success granted)
            {
                return granted.Data?.Granted == true
                    ? LocalNotificationPermissionResult.Granted()
                    : LocalNotificationPermissionResult.Denied();
            }

            var classification = SdkResultClassification.Of(asked);
            return LocalNotificationPermissionResult.Failed(
                classification.Problem ?? new HiveError(
                    HiveErrorCode.Unknown,
                    $"The OS did not answer the permission ask: {classification.UnknownCode}"));
        }

        internal static string IdOf(int id) => k_IdPrefix + id.ToString(CultureInfo.InvariantCulture);

        // Ids must stay unique across sources and app runs with no state to persist, so each is
        // a fresh random draw; a collision would replace a pending notification, at odds of
        // n/2^31 against what this source has pending. Non-negative keeps the OS id recipe-N.
        private static int NextId() => Guid.NewGuid().GetHashCode() & int.MaxValue;

        // Null for a success, the preserved error otherwise.
        private static HiveError Problem(IAxylResult result)
        {
            var classification = SdkResultClassification.Of(result);
            return classification.Kind == SdkResultKind.Success
                ? null
                : classification.Problem ?? new HiveError(
                    HiveErrorCode.Unknown, $"The OS did not answer: {classification.UnknownCode}");
        }
    }
}

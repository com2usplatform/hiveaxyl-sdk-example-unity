// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Checks notification permission and schedules a notification when permission is granted.
    /// The permission check may show an OS prompt.
    /// </summary>
    /// <remarks>
    /// What the Recipe needs depends on the source: <see cref="UnityLocalNotificationSource"/>
    /// (Android/iOS) requires Unity Mobile Notifications and no SDK initialization, while
    /// <see cref="AppleLocalNotificationSource"/> (macOS) requires the SDK's Apple
    /// local-notification add-on, resolved after SDK initialization.
    /// Permission is shared with push notifications. A non-granted result schedules nothing
    /// and does not establish whether another request will show a prompt.
    /// Retain the returned notification ID to cancel it later.
    /// </remarks>
    public sealed class LocalNotificationRecipe
    {
        private readonly ILocalNotificationSource m_source;

        /// <summary>Creates the Recipe over the platform's half.</summary>
        /// <param name="source">
        /// The OS permission ask and scheduler. Must not be null. A game ships
        /// <see cref="UnityLocalNotificationSource"/>.
        /// </param>
        public LocalNotificationRecipe(ILocalNotificationSource source)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>
        /// Checks or requests notification permission.
        /// Whether the OS shows a prompt depends on the platform and permission state.
        /// </summary>
        /// <param name="cancellationToken">Stops the wait. The OS dialog itself cannot be withdrawn.</param>
        public async Task<LocalNotificationOutcome> RequestPermissionAsync(
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(LocalNotificationStep.Validate, "before the ask");
            }

            var asked = await m_source.RequestPermissionAsync(cancellationToken);

            // Re-checked after every await: a cancel does not reach into the dialog, so its
            // answer can land after the caller stopped waiting — and must not read as one.
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(LocalNotificationStep.Permission, "during the ask");
            }

            return Translate(asked);
        }

        /// <summary>
        /// Validates the request, checks permission, and schedules one notification if granted.
        /// On success, pass <see cref="LocalNotificationOutcome.Id"/> to <see cref="CancelAsync"/>
        /// to cancel the reservation.
        /// </summary>
        /// <param name="request">What to show and when. Must not be null.</param>
        /// <param name="cancellationToken">Stops the wait for the permission ask.</param>
        public async Task<LocalNotificationOutcome> ScheduleAsync(
            LocalNotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(LocalNotificationStep.Validate, "before the ask");
            }

            var refused = Validate(request);
            if (refused != null)
            {
                return refused;
            }

            var asked = await m_source.RequestPermissionAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(LocalNotificationStep.Permission, "during the ask");
            }

            var permission = Translate(asked);
            if (permission.Status != LocalNotificationStatus.Success)
            {
                // A denial schedules nothing: the OS would drop the notification silently, and
                // the game would wait for a reminder that never comes.
                return permission;
            }

            var scheduled = await m_source.ScheduleAsync(request, cancellationToken);
            if (scheduled.IsSuccess)
            {
                // Even when a cancel landed during the call: the OS holds the reservation, a
                // cancel here cannot unmake it, and the id is the one handle that can — so it
                // must reach the caller rather than be traded for a cancellation report.
                return LocalNotificationOutcome.Succeeded(scheduled.Id);
            }

            return cancellationToken.IsCancellationRequested
                ? Canceled(LocalNotificationStep.Schedule, "while scheduling")
                : LocalNotificationOutcome.Failed(LocalNotificationStep.Schedule, scheduled.Error);
        }

        /// <summary>
        /// Withdraws the scheduled notification with <paramref name="id"/>. An id that is no longer
        /// scheduled — shown already, or never scheduled — is a no-op, not an error.
        /// </summary>
        public async Task<LocalNotificationOutcome> CancelAsync(
            int id, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(LocalNotificationStep.Cancel, "before the cancel");
            }

            var error = await m_source.CancelAsync(id, cancellationToken);
            return cancellationToken.IsCancellationRequested
                ? Canceled(LocalNotificationStep.Cancel, "during the cancel")
                : TranslateCancel(error);
        }

        /// <summary>
        /// Withdraws scheduled notifications, in the scope the source defines:
        /// <see cref="UnityLocalNotificationSource"/> withdraws every notification the app
        /// scheduled, the game's own included (the package offers no narrower cancel), while
        /// <see cref="AppleLocalNotificationSource"/> withdraws only what it scheduled itself.
        /// </summary>
        public async Task<LocalNotificationOutcome> CancelAllAsync(
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(LocalNotificationStep.Cancel, "before the cancel");
            }

            var error = await m_source.CancelAllAsync(cancellationToken);
            return cancellationToken.IsCancellationRequested
                ? Canceled(LocalNotificationStep.Cancel, "during the cancel")
                : TranslateCancel(error);
        }

        // The refusals that need no call to discover. Answers null when the request is sound.
        private static LocalNotificationOutcome Validate(LocalNotificationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Title))
            {
                return LocalNotificationOutcome.Failed(
                    LocalNotificationStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "A local notification needs a title. Nothing was scheduled."));
            }

            // A fire time already behind us would show the notification at once, which no caller
            // means. The clock it is held to is the one it was written against: a UTC time is
            // measured against UTC, anything else against the device's local time.
            var now = request.FireAt.Kind == DateTimeKind.Utc ? DateTime.UtcNow : DateTime.Now;
            if (request.FireAt <= now)
            {
                return LocalNotificationOutcome.Failed(
                    LocalNotificationStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "The fire time must be in the future. Nothing was scheduled."));
            }

            return null;
        }

        private static LocalNotificationOutcome Translate(LocalNotificationPermissionResult asked)
        {
            switch (asked.Status)
            {
                case LocalNotificationPermissionStatus.Granted:
                    return LocalNotificationOutcome.Succeeded();

                case LocalNotificationPermissionStatus.Denied:
                    // Permission was not granted. This result does not distinguish an explicit
                    // refusal from other reasons permission remains unavailable.
                    return LocalNotificationOutcome.DeniedByPlayer();

                default:
                    return LocalNotificationOutcome.Failed(LocalNotificationStep.Permission, asked.Error);
            }
        }

        private static LocalNotificationOutcome TranslateCancel(HiveError error) =>
            error == null
                ? LocalNotificationOutcome.Succeeded()
                : LocalNotificationOutcome.Failed(LocalNotificationStep.Cancel, error);

        private static LocalNotificationOutcome Canceled(LocalNotificationStep step, string when) =>
            LocalNotificationOutcome.Failed(
                step, new HiveError(HiveErrorCode.Cancelled, $"The caller canceled {when}."));
    }
}

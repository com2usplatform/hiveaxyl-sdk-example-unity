// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>
    /// Three independent scenarios. With <c>UnityLocalNotificationSource</c> (Android / iOS) no SDK
    /// initialization is needed; <c>AppleLocalNotificationSource</c> (macOS) takes the add-on
    /// plugin, which the initialized registry resolves.
    /// </summary>
    public static class LocalNotificationExamples
    {
        public static async Task<LocalNotificationOutcome> RequestPermissionAsync(
            ILocalNotificationSource source, Action restoreNotificationDelegate,
            CancellationToken cancellationToken = default)
        {
            if (restoreNotificationDelegate == null)
            {
                throw new ArgumentNullException(nameof(restoreNotificationDelegate));
            }
            try
            {
                var outcome = await new LocalNotificationRecipe(source).RequestPermissionAsync(cancellationToken);
                switch (outcome.Status)
                {
                    case LocalNotificationStatus.Success:
                        // APP: scheduling is now available. Do not schedule without the player's intent.
                        break;
                    case LocalNotificationStatus.PermissionDenied:
                        // APP: permission was not granted. Do not assume permanent denial;
                        // offer OS settings guidance when appropriate.
                        break;
                    default:
                        // APP: inspect Error/FailedStep, including unsupported platform and cancellation.
                        break;
                }
                return outcome;
            }
            finally
            {
                // APP: re-install the APNs forwarder on iOS if your game also receives remote push.
                restoreNotificationDelegate();
            }
        }

        public static async Task<LocalNotificationOutcome> ScheduleAsync(
            ILocalNotificationSource source, string title, string body, DateTime fireAt,
            Action restoreNotificationDelegate, CancellationToken cancellationToken = default)
        {
            if (restoreNotificationDelegate == null)
            {
                throw new ArgumentNullException(nameof(restoreNotificationDelegate));
            }
            // APP: choose a future time. DateTimeKind.Utc is UTC; Local and Unspecified are local time.
            // Source creation configures the Android notification channel.
            var request = new LocalNotificationRequest(title, body, fireAt);
            try
            {
                var outcome = await new LocalNotificationRecipe(source).ScheduleAsync(request, cancellationToken);
                if (outcome.Status == LocalNotificationStatus.Success)
                {
                    // APP: persist outcome.Id so the same notification can be canceled later.
                }
                else if (outcome.Status == LocalNotificationStatus.PermissionDenied)
                {
                    // Nothing was scheduled; let the player choose whether to change OS settings.
                }
                else
                {
                    // APP: report Error/FailedStep; do not store an invalid notification id.
                }
                return outcome;
            }
            finally
            {
                // Scheduling also checks permission and may show an OS prompt.
                restoreNotificationDelegate();
            }
        }

        public static async Task<LocalNotificationOutcome> CancelAsync(
            ILocalNotificationSource source, int savedId, CancellationToken cancellationToken)
        {
            var outcome = await new LocalNotificationRecipe(source).CancelAsync(savedId, cancellationToken);
            if (outcome.Status == LocalNotificationStatus.Success)
            {
                // APP: remove this id from your saved reminders.
            }
            else
            {
                // APP: retain the id and inspect Error; do not claim the reminder was canceled.
            }
            return outcome;
        }

        public static async Task<LocalNotificationOutcome> CancelAllAsync(
            ILocalNotificationSource source, CancellationToken cancellationToken)
        {
            // APP: call only for an intentional reset. The scope is the source's:
            // UnityLocalNotificationSource (Android/iOS) withdraws EVERY notification this app
            // scheduled, the ones outside this Recipe included, while AppleLocalNotificationSource
            // (macOS) withdraws only what the Recipe scheduled.
            var outcome = await new LocalNotificationRecipe(source).CancelAllAsync(cancellationToken);
            if (outcome.Status == LocalNotificationStatus.Success)
            {
                // APP: clear your saved reminder ids.
            }
            // Otherwise retain saved ids and handle outcome.Error.
            return outcome;
        }
    }
}

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>Three independent scenarios; local notifications do not require SDK initialization.</summary>
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

        public static LocalNotificationOutcome Cancel(ILocalNotificationSource source, int savedId)
        {
            var outcome = new LocalNotificationRecipe(source).Cancel(savedId);
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

        public static LocalNotificationOutcome CancelAll(ILocalNotificationSource source)
        {
            // APP: call only for an intentional reset of ALL local notifications owned by this app.
            var outcome = new LocalNotificationRecipe(source).CancelAll();
            if (outcome.Status == LocalNotificationStatus.Success)
            {
                // APP: clear your saved reminder ids.
            }
            // Otherwise retain saved ids and handle outcome.Error.
            return outcome;
        }
    }
}

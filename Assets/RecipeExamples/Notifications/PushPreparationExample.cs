// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>Prepare push once; the application's lifetime owner handles events and refreshes.</summary>
    public static class PushPreparationExample
    {
        // Before calling: complete SDK/native setup, create the source, and subscribe to token/reception events.
        // Keep the source for the feature's lifetime and serialize preparation calls.
        // During preparation, retain the latest event token; after success, rerun only if it differs from the
        // registered token.
        public static async Task<PreparePushOutcome> RunAsync(
            IPushTokenSource source, PushPreparation preparation, Action restoreNotificationDelegate,
            CancellationToken cancellationToken = default)
        {
            if (restoreNotificationDelegate == null)
            {
                throw new ArgumentNullException(nameof(restoreNotificationDelegate));
            }
            PreparePushOutcome outcome;
            try
            {
                outcome = await new PushRecipe(source).PrepareAsync(preparation, cancellationToken);
            }
            finally
            {
                // APP: iOS re-installs the APNs forwarder after Unity's permission request.
                // Supply a no-op on platforms without that delegate. See Initialization.md.
                restoreNotificationDelegate();
            }
            switch (outcome.Status)
            {
                case PreparePushStatus.Success:
                    // Token registration succeeded. Handle notification events and startup notifications
                    // separately in the application. See Initialization.md.
                    break;
                case PreparePushStatus.PermissionDenied:
                    // APP: explain how to change OS settings; do not repeatedly prompt.
                    break;
                case PreparePushStatus.BusinessOutcome:
                    // APP: handle the named registration refusal; retain unknown outcome details.
                    break;
                default:
                    // APP: inspect Error/FailedStep; preserve caller cancellation as cancellation.
                    break;
            }
            // APP: on feature shutdown, stop/await queued work, unsubscribe your event handlers,
            // then Dispose the source. This one-call method does not take ownership of the source.
            return outcome;
        }
    }
}

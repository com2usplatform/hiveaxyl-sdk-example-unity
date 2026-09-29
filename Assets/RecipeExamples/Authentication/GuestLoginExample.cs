// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>
    /// Shows how to create or restore a guest account and when to save returned guest credentials.
    /// </summary>
    public static class GuestLoginExample
    {
        public static async Task<LoginAsGuestOutcome> RunAsync(
            string clientId, string deviceKey, GuestCredential storedGuest,
            CancellationToken cancellationToken = default)
        {
            // Load storedGuest from secure storage; null explicitly means create a guest.
            // Keep any returned Credential even if a later session-setup step fails.
            // Otherwise a retry could create another account instead of restoring this guest.
            var recipe = new GuestLoginRecipe(clientId, deviceKey);
            var outcome = await recipe.LoginAsGuestAsync(storedGuest, cancellationToken);
            if (outcome.Credential != null)
            {
                // APP: persist this guest credential securely before offering a retry.
                // This identity is separate from the access/refresh tokens for auto login.
            }

            switch (outcome.Status)
            {
                case LoginAsGuestStatus.Success:
                    // APP: inspect IsBlocked before allowing gameplay, then persist the current
                    // session securely (including refreshed tokens). See Initialization.md.
                    break;
                case LoginAsGuestStatus.BusinessOutcome:
                    // APP: handle BusinessOutcome; see README.md for unknown response handling.
                    break;
                default:
                    // APP: inspect Error and FailedStep. Caller cancellation is distinct from
                    // a network problem; offer recovery deliberately instead of retrying blindly.
                    break;
            }
            return outcome;
        }
    }
}

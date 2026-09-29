// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>
    /// Shows how to log in with a one-time grant key from the game server and handle a rejected key.
    /// </summary>
    public static class CustomLoginExample
    {
        public static async Task<LoginWithCustomOutcome> RunAsync(
            string clientId, string deviceKey, string grantKey,
            CancellationToken cancellationToken = default)
        {
            // First authenticate with YOUR game server and obtain its one-time grant key.
            // The app must never mint this key or contain server credentials.
            var recipe = new CustomLoginRecipe(clientId, deviceKey);
            var outcome = await recipe.LoginWithCustomAsync(grantKey, cancellationToken);

            switch (outcome.Status)
            {
                case LoginWithCustomStatus.Success:
                    // APP: inspect IsBlocked before allowing gameplay, then persist the current
                    // session securely (including refreshed tokens). See Initialization.md.
                    break;
                case LoginWithCustomStatus.BusinessOutcome:
                    // APP: if IsGrantKeyRejected is true, obtain a fresh key from your server.
                    // False does not guarantee that the original key can be reused.
                    // A new login after failed token exchange requires a fresh grant key.
                    // Handle BusinessOutcome; see README.md for unknown response handling.
                    break;
                default:
                    // APP: inspect Error and FailedStep; distinguish cancellation from failure.
                    // The grant key may already have been redeemed even though login did not finish.
                    // Do not automatically retry with a key whose redemption status is uncertain.
                    break;
            }
            return outcome;
        }
    }
}

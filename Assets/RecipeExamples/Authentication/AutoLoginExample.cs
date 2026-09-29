// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>
    /// Shows how to restore a saved session and handle stale stored credentials.
    /// </summary>
    public static class AutoLoginExample
    {
        public static async Task<AutoLoginOutcome> RunAsync(
            string clientId, StoredSession storedSession,
            CancellationToken cancellationToken = default)
        {
            // Read storedSession from secure storage. Pass null when none was saved.
            // Do not delete stored credentials solely because login failed; check StoredCredentialIsStale.
            var recipe = new AutoLoginRecipe(clientId);
            var outcome = await recipe.LoginAsync(storedSession, cancellationToken);
            // Staleness also accompanies NoSession, so inspect it BEFORE the early return.
            if (outcome.StoredCredentialIsStale)
            {
                // APP: retire only the stale saved session and ask the player to log in again.
            }
            if (outcome.Status == AutoLoginStatus.NoSession)
            {
                // APP: offer an explicit login choice; there is no session to continue with.
                return outcome;
            }

            switch (outcome.Status)
            {
                case AutoLoginStatus.Success:
                    // APP: inspect IsBlocked before allowing game access.
                    // Null means the block status was not checked; query it separately if required.
                    // Persist the current session securely, including refreshed tokens.
                    // See Initialization.md.
                    break;
                case AutoLoginStatus.BusinessOutcome:
                    // APP: handle BusinessOutcome; see README.md for unknown response handling.
                    break;
                default:
                    // APP: inspect Error and FailedStep; distinguish cancellation from failure.
                    // StoredCredentialIsStale == false does not guarantee token reuse.
                    // Do not automatically retry a refresh whose server-side result is uncertain.
                    break;
            }
            return outcome;
        }
    }
}

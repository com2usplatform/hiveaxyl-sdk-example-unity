// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>
    /// Shows how to link a provider to the current account and clean up that account's guest credentials after a
    /// successful link.
    /// </summary>
    public static class LinkProviderExample
    {
        public static async Task<LinkProviderOutcome> RunAsync(
            IProviderCredentialSource source,
            CancellationToken cancellationToken = default)
        {
            // First sign in to the account being linked. Use the same source adapters as login.
            // Serialize account operations so the session cannot change during linking.
            var recipe = new LinkProviderRecipe();
            var outcome = await recipe.LinkProviderAsync(source, cancellationToken);

            switch (outcome.Status)
            {
                case LinkProviderStatus.Success:
                    // APP: refresh the account's connected-provider display.
                    // Discard a stored GuestCredential only when its PlayerId matches outcome.PlayerId.
                    // The first provider link invalidates that player's guest token.
                    // Keep credentials belonging to other players.
                    break;
                case LinkProviderStatus.UserCanceled:
                    // APP: return to the previous choice; declining consent is not a failure.
                    break;
                case LinkProviderStatus.BusinessOutcome:
                    // APP: ProviderAlreadyConnected means the requested link already exists.
                    // ProviderOwnedByOther and ProviderTypeAlreadyExists need an explicit player decision.
                    // Never switch accounts or unlink another provider automatically.
                    // See README.md for unknown response handling.
                    break;
                default:
                    // APP: inspect Error and FailedStep; distinguish cancellation from failure.
                    // Cancellation does not undo a link the server already completed.
                    // If the request may have reached the server, reload the player's linked providers
                    // before deciding whether to retry.
                    break;
            }
            return outcome;
        }
    }
}

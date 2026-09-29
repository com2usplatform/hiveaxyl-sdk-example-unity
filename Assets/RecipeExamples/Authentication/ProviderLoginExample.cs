// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>
    /// Shows how to log in through a provider credential adapter and handle success, user cancellation, and
    /// failure.
    /// </summary>
    public static class ProviderLoginExample
    {
        public static async Task<LoginWithProviderOutcome> RunAsync(
            string clientId, string deviceKey, IProviderCredentialSource source,
            CancellationToken cancellationToken = default)
        {
            // Construct one provider adapter after its platform prerequisites are ready.
            // See Adapters.md. The recipe obtains credentials and establishes the session;
            // do not acquire provider credentials separately or duplicate this flow per provider.
            var recipe = new ProviderLoginRecipe(clientId, deviceKey);
            var outcome = await recipe.LoginWithProviderAsync(source, cancellationToken);

            switch (outcome.Status)
            {
                case LoginWithProviderStatus.Success:
                    // APP: inspect IsBlocked before allowing gameplay, then persist the current
                    // session securely (including refreshed tokens). See Initialization.md.
                    break;
                case LoginWithProviderStatus.UserCanceled:
                    // APP: return to the previous choice; declining consent is not a failure.
                    break;
                case LoginWithProviderStatus.BusinessOutcome:
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

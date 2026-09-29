// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>
    /// Shows how to log in or sign up with a username and password and handle newly created accounts.
    /// </summary>
    public static class UsernameLoginExample
    {
        public static async Task<LoginWithUsernameOutcome> RunAsync(
            string clientId, string deviceKey, string username, string password, string grantKey,
            CancellationToken cancellationToken = default)
        {
            // Obtain the player's input and, if needed, a server-issued signup grant key.
            // This flow attempts login first, then signup only on UsernameVerifyFailed.
            // A mistyped, unused username can therefore create a new account.
            // Supply the password to the recipe; it performs the required hashing.
            // Never log or persist the password.
            var recipe = new UsernameLoginRecipe(clientId, deviceKey);
            var outcome = await recipe.LogInOrSignUpAsync(username, password, grantKey, cancellationToken);

            switch (outcome.Status)
            {
                case LoginWithUsernameStatus.Success:
                    // APP: inspect IsNewAccount and explain when this attempt created an account.
                    // If the player expected an existing account, ask them to check the username.
                    // Inspect IsBlocked before allowing gameplay, then persist the current
                    // session securely (including refreshed tokens). See Initialization.md.
                    break;
                case LoginWithUsernameStatus.BusinessOutcome:
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

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Samples.Recipes;

namespace Hive.Axyl.Samples.RecipeExamples
{
    /// <summary>
    /// Shows how to end a session and clean up its saved session data only when SessionCleared is true.
    /// </summary>
    public static class LogoutExample
    {
        public static async Task<LogoutOutcome> RunAsync(CancellationToken cancellationToken = default)
        {
            // APP: serialize login/logout and remember which account this operation belongs to.
            var outcome = await new LogoutRecipe().LogoutAsync(cancellationToken);
            if (outcome.SessionCleared)
            {
                // APP: delete that account's saved session and navigate to login.
                // This may also be true when cancellation arrived after server-side logout.
                // Do not erase an unrelated guest identity or a newly signed-in account.
            }
            switch (outcome.Status)
            {
                case LogoutStatus.Success:
                    // The server ended the session. SessionCleared governs local persistence.
                    break;
                case LogoutStatus.BusinessOutcome:
                    // APP: explain the named refusal, e.g. GuestSignoutBlocked. Keep the session.
                    break;
                default:
                    // APP: inspect Error/FailedStep; a timeout alone is not permission to sign out.
                    break;
            }
            return outcome;
        }
    }
}

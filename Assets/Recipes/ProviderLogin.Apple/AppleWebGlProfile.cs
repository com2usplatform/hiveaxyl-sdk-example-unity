// Copyright (c) Com2uS Platform Corp. All rights reserved.

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>Profile supplied by Apple's browser authorization response.</summary>
    /// <remarks>
    /// This data has not been verified by the application backend. Persist it only after
    /// associating it with a verified account. Apple sends the name only on first authorization;
    /// preserve it across a failed server exchange rather than expecting it on a later login.
    /// </remarks>
    public sealed class AppleWebGlProfile
    {
        internal AppleWebGlProfile(string firstName, string lastName, string email, bool firstAuthorization)
        {
            FirstName = firstName;
            LastName = lastName;
            Email = email;
            IsFirstAuthorization = firstAuthorization;
        }

        /// <summary>The given name, or empty when Apple omitted it.</summary>
        public string FirstName { get; }

        /// <summary>The family name, or empty when Apple omitted it.</summary>
        public string LastName { get; }

        /// <summary>The email from the user object or unverified identity-token claim, or empty.</summary>
        public string Email { get; }

        /// <summary>Whether Apple included its one-time user object in this response.</summary>
        public bool IsFirstAuthorization { get; }
    }
}

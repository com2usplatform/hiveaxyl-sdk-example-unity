// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Security.Cryptography;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// A fresh, unpredictable nonce for one sign-in attempt: 32 random bytes as base64url.
    /// </summary>
    /// <remarks>
    /// The provider writes it into the token it issues, which is what ties that token to this
    /// attempt and to no other — so it comes from a CSPRNG and is never reused. It is handed over
    /// raw: Google embeds the string as is, Apple is given <see cref="Pkce.Sha256Hex"/> of it, so
    /// hashing is the caller's decision, not this helper's.
    /// </remarks>
    public static class Nonce
    {
        /// <summary>Returns a new nonce. Every call returns a different value.</summary>
        public static string New()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}

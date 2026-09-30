// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>A PKCE verifier and its S256 code challenge.</summary>
    public readonly struct PkceCodes
    {
        /// <summary>Pairs a verifier with the challenge derived from it.</summary>
        public PkceCodes(string verifier, string challenge)
        {
            Verifier = verifier;
            Challenge = challenge;
        }

        /// <summary>The high-entropy verifier, kept until the token exchange.</summary>
        public string Verifier { get; }

        /// <summary>The S256 challenge sent with the authorization request.</summary>
        public string Challenge { get; }
    }

    /// <summary>
    /// PKCE (RFC 7636) helpers shared by the Recipes that start an authorization-code flow. PKCE
    /// generation is the App's responsibility; the SDK only forwards the values it is given.
    /// </summary>
    public static class Pkce
    {
        /// <summary>
        /// Generates a fresh verifier (32 random bytes, base64url) and its S256 challenge. Each call
        /// returns a new pair; one pair belongs to exactly one authorization attempt.
        /// </summary>
        public static PkceCodes Create()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var verifier = Base64Url(bytes);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.ASCII.GetBytes(verifier));
                return new PkceCodes(verifier, Base64Url(hash));
            }
        }

        /// <summary>SHA-256 of the input's ASCII bytes, lowercase hex.</summary>
        /// <remarks>
        /// ASCII, because every input this hashes is ASCII by construction — a PKCE verifier, whose
        /// charset RFC 7636 restricts, or a base64url nonce. For arbitrary text, and passwords in
        /// particular, use <see cref="PasswordHash.Sha256Hex"/>: ASCII silently rewrites every
        /// non-ASCII character as <c>?</c> instead of failing.
        /// </remarks>
        public static string Sha256Hex(string input)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.ASCII.GetBytes(input ?? string.Empty));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }

        private static string Base64Url(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

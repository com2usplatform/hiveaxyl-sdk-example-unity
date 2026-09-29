// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Security.Cryptography;
using System.Text;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Turns a typed password into the value the Username endpoints accept: lowercase-hex SHA-256
    /// over the password's UTF-8 bytes.
    /// </summary>
    /// <remarks>
    /// Use UTF-8 consistently across clients. Pass the original password to the Username Recipe;
    /// it applies this hash before sending. Never log passwords.
    /// </remarks>
    internal static class PasswordHash
    {
        /// <summary>
        /// Hashes <paramref name="password"/> as lowercase-hex SHA-256 of its UTF-8 bytes.
        /// </summary>
        /// <param name="password">
        /// The password exactly as the player typed it. Never trimmed or case-folded here — doing so
        /// would change the password rather than normalize it.
        /// </param>
        internal static string Sha256Hex(string password)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(password ?? string.Empty));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    builder.Append(b.ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}

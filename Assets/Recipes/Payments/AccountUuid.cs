// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Derives the <c>accountUuid</c> a purchase carries, from the game's namespace and the player id.
    /// </summary>
    /// <remarks>
    /// Use the same namespace and player ID representation across all devices and platforms.
    /// Choose the namespace once and never change it. Use the player-ID overload for account identifiers.
    /// </remarks>
    public static class AccountUuid
    {
        /// <summary>
        /// Returns the player's UUIDv5 as an RFC 4122 string.
        /// </summary>
        /// <param name="namespaceId">The game's namespace. Chosen once and never changed.</param>
        /// <param name="playerId">The player id, hashed as its decimal spelling.</param>
        public static string Compute(Guid namespaceId, long playerId) =>
            Compute(namespaceId, playerId.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// Computes a UUIDv5 for an arbitrary name. For player identifiers, use the <c>long</c> overload.
        /// </summary>
        /// <param name="namespaceId">The namespace to hash under.</param>
        /// <param name="name">The name to hash. Must not be null.</param>
        public static string Compute(Guid namespaceId, string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            var namespaceBytes = ToNetworkOrder(namespaceId.ToByteArray());
            var nameBytes = Encoding.UTF8.GetBytes(name);

            var input = new byte[namespaceBytes.Length + nameBytes.Length];
            Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
            Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);

            byte[] hash;
            using (var sha1 = SHA1.Create())
            {
                hash = sha1.ComputeHash(input);
            }

            // The first 16 of SHA-1's 20 bytes, with six bits overwritten: the version nibble says 5, and
            // the variant says RFC 4122. Everything else is left as the hash produced it.
            var uuid = new byte[16];
            Buffer.BlockCopy(hash, 0, uuid, 0, uuid.Length);
            uuid[6] = (byte)((uuid[6] & 0x0F) | 0x50);
            uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80);

            return new Guid(ToNetworkOrder(uuid)).ToString("D", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The UUIDv5 for a player under a namespace written as text, or <c>false</c> when that text is
        /// not a UUID. The namespace reaches this app as a string, and a typo in it must not become a
        /// silently different account.
        /// </summary>
        /// <param name="namespaceText">The namespace as an RFC 4122 string.</param>
        /// <param name="playerId">The player id, hashed as its decimal spelling.</param>
        /// <param name="accountUuid">The UUID, or empty when the namespace could not be read.</param>
        public static bool TryCompute(string namespaceText, long playerId, out string accountUuid)
        {
            if (!Guid.TryParse(namespaceText, out var namespaceId))
            {
                accountUuid = string.Empty;
                return false;
            }

            accountUuid = Compute(namespaceId, playerId);
            return true;
        }

        // Swaps the three fields .NET stores little-endian: the 4-byte time_low, then the two 2-byte
        // fields. The last eight bytes are already in order. An involution — the same swap converts
        // either way, which is why it appears on both sides of the hash.
        private static byte[] ToNetworkOrder(byte[] guidBytes)
        {
            var swapped = (byte[])guidBytes.Clone();
            Swap(swapped, 0, 3);
            Swap(swapped, 1, 2);
            Swap(swapped, 4, 5);
            Swap(swapped, 6, 7);
            return swapped;
        }

        private static void Swap(byte[] bytes, int left, int right)
        {
            var held = bytes[left];
            bytes[left] = bytes[right];
            bytes[right] = held;
        }
    }
}

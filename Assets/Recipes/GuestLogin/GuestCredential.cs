// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// What the app must keep to log the same Guest back in on a later launch. The SDK does not
    /// persist it: storing and reloading this pair is the app's responsibility, and losing it means
    /// losing the Guest account.
    /// </summary>
    public sealed class GuestCredential
    {
        private GuestCredential(long playerId, string guestToken)
        {
            PlayerId = playerId;
            GuestToken = guestToken;
        }

        /// <summary>The player the Guest account resolved to.</summary>
        public long PlayerId { get; }

        /// <summary>The re-login secret the server issued when the Guest account was created.</summary>
        public string GuestToken { get; }

        /// <summary>
        /// Creates a credential from a persisted pair.
        /// </summary>
        /// <param name="playerId">The stored player id. Must be positive.</param>
        /// <param name="guestToken">The stored Guest token. Must not be null or whitespace.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="playerId"/> is not positive.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="guestToken"/> is null, empty, or whitespace.
        /// </exception>
        public static GuestCredential Create(long playerId, string guestToken)
        {
            if (playerId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerId), playerId, "playerId must be positive.");
            }

            if (string.IsNullOrWhiteSpace(guestToken))
            {
                throw new ArgumentException(
                    "guestToken must be a non-empty, non-whitespace string.", nameof(guestToken));
            }

            return new GuestCredential(playerId, guestToken);
        }
    }
}

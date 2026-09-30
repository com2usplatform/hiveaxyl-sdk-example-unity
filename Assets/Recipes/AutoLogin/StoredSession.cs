// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// What an app has to keep from a previous run to restore the session on the next one.
    /// </summary>
    /// <remarks>
    /// The app must persist and reload these credentials using secure storage. Never log the tokens.
    /// </remarks>
    public sealed class StoredSession
    {
        private StoredSession(string accessToken, string refreshToken, long playerId)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            PlayerId = playerId;
        }

        /// <summary>The access token the last run ended with. May be expired.</summary>
        public string AccessToken { get; }

        /// <summary>The refresh token the last run ended with.</summary>
        public string RefreshToken { get; }

        /// <summary>The player the tokens belong to.</summary>
        public long PlayerId { get; }

        /// <summary>
        /// Creates the credential to restore from.
        /// </summary>
        /// <param name="accessToken">
        /// The stored access token, or null when only a refresh token was kept. An expired one is
        /// fine — the Recipe reads its expiry and takes the refresh route instead.
        /// </param>
        /// <param name="refreshToken">
        /// The stored refresh token, or null when only an access token was kept. Without it an
        /// expired access token leaves nothing to restore from.
        /// </param>
        /// <param name="playerId">The player the tokens belong to. Must be positive.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when both tokens are absent or <paramref name="playerId"/> is not positive.
        /// </exception>
        public static StoredSession Create(string accessToken, string refreshToken, long playerId)
        {
            if (string.IsNullOrWhiteSpace(accessToken) && string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new ArgumentException(
                    "A stored session needs at least one of accessToken or refreshToken. "
                    + "Pass null to AutoLoginRecipe instead of an empty credential.",
                    nameof(accessToken));
            }

            if (playerId <= 0)
            {
                throw new ArgumentException(
                    "A stored session names the player its tokens belong to; zero or less names "
                    + "nobody.",
                    nameof(playerId));
            }

            return new StoredSession(accessToken, refreshToken, playerId);
        }
    }
}

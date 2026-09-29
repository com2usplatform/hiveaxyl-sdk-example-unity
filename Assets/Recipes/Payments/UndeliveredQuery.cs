// Copyright (c) Com2uS Platform Corp. All rights reserved.

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// What finding undelivered purchases needs from the app: the player's slice of the world, since
    /// the purchases themselves are the server's to name.
    /// </summary>
    public sealed class UndeliveredQuery
    {
        /// <summary>Creates the query.</summary>
        /// <param name="country">
        /// Country code. Must not be blank.
        /// </param>
        /// <param name="language">
        /// Language code. Must not be blank.
        /// </param>
        /// <param name="serverId">
        /// Optional game server ID. Limits server-side recovery to that server; store results are not
        /// filtered by it.
        /// </param>
        /// <param name="appVersion">App version (optional).</param>
        /// <param name="accountUuid">
        /// The account UUID the recording calls send (optional). Travels onto each recovered
        /// purchase's order so the verify call carries it, the same as a normal purchase.
        /// </param>
        public UndeliveredQuery(
            string country,
            string language,
            string serverId = null,
            string appVersion = null,
            string accountUuid = null)
        {
            Country = country;
            Language = language;
            ServerId = serverId;
            AppVersion = appVersion;
            AccountUuid = accountUuid;
        }

        /// <summary>Country code.</summary>
        public string Country { get; }

        /// <summary>Language code.</summary>
        public string Language { get; }

        /// <summary>Game server id, or null for all servers.</summary>
        public string ServerId { get; }

        /// <summary>App version, or null.</summary>
        public string AppVersion { get; }

        /// <summary>The account UUID to carry onto the recovered purchases, or null.</summary>
        public string AccountUuid { get; }
    }
}

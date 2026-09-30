// Copyright (c) Com2uS Platform Corp. All rights reserved.

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// What registering this device for push needs beyond the token itself: where and how the
    /// campaign filters will match it, and what the player has agreed to receive.
    /// </summary>
    /// <remarks>
    /// Registration is device-scoped and remains after logout. Supply the device locale and user consent.
    /// </remarks>
    public sealed class PushPreparation
    {
        /// <summary>Declares the registration.</summary>
        /// <param name="language">
        /// The message language, as a BCP-47-ish code the registry names — "ko", "en", "zh-Hans".
        /// Must name a language the registry knows.
        /// </param>
        /// <param name="country">The device's country, ISO 3166-1 alpha-2. Must not be blank.</param>
        /// <param name="timezoneId">
        /// The device's IANA time zone name — "Asia/Seoul". Must not be blank; the server refuses a
        /// name it does not know.
        /// </param>
        /// <param name="agreedToInfo">Consent to informational pushes.</param>
        /// <param name="agreedToAdvertising">Consent to advertising pushes.</param>
        /// <param name="agreedToNightAdvertising">
        /// Consent to nighttime advertising. Cannot be true without
        /// <paramref name="agreedToAdvertising"/> — the registry refuses that shape, so this Recipe
        /// does too, before any call is made.
        /// </param>
        /// <param name="serverId">The game server this device plays on, when the game has one.</param>
        /// <param name="appVersion">The app version, for the registry's records.</param>
        public PushPreparation(
            string language,
            string country,
            string timezoneId,
            bool agreedToInfo = true,
            bool agreedToAdvertising = false,
            bool agreedToNightAdvertising = false,
            string serverId = null,
            string appVersion = null)
        {
            Language = language;
            Country = country;
            TimezoneId = timezoneId;
            AgreedToInfo = agreedToInfo;
            AgreedToAdvertising = agreedToAdvertising;
            AgreedToNightAdvertising = agreedToNightAdvertising;
            ServerId = serverId;
            AppVersion = appVersion;
        }

        /// <summary>The message language code — "ko", "en", "zh-Hans".</summary>
        public string Language { get; }

        /// <summary>The device's country, ISO 3166-1 alpha-2.</summary>
        public string Country { get; }

        /// <summary>The device's IANA time zone name — "Asia/Seoul".</summary>
        public string TimezoneId { get; }

        /// <summary>Consent to informational pushes.</summary>
        public bool AgreedToInfo { get; }

        /// <summary>Consent to advertising pushes.</summary>
        public bool AgreedToAdvertising { get; }

        /// <summary>Consent to nighttime advertising. Requires <see cref="AgreedToAdvertising"/>.</summary>
        public bool AgreedToNightAdvertising { get; }

        /// <summary>The game server this device plays on, or null.</summary>
        public string ServerId { get; }

        /// <summary>The app version, or null.</summary>
        public string AppVersion { get; }
    }
}

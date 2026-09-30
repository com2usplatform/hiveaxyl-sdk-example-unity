// Copyright (c) Com2uS Platform Corp. All rights reserved.

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// The business results a provider login can end in, translated from the SDK Outcomes of every
    /// step into one vocabulary. Branch on these values, never on a message string.
    /// </summary>
    public enum ProviderLoginBusinessOutcome
    {
        /// <summary>
        /// An Outcome the Recipe does not translate. Two cases, told apart by the outcome's
        /// unknown-code property: non-empty means the server sent a code this SDK build does not
        /// know; empty means the SDK typed the Outcome but the Recipe has no value for it yet.
        /// Never guess a known value from either.
        /// </summary>
        Unrecognized = 0,

        /// <summary>The server does not support logging in with this provider.</summary>
        ProviderNotSupported,

        /// <summary>The server does not exchange authorization codes for this provider.</summary>
        ProviderTokenExchangeNotSupported,

        /// <summary>The provider rejected the token or ticket the client presented.</summary>
        ProviderTokenError,

        /// <summary>The server could not reach the provider, or the provider answered with an error.</summary>
        ProviderRequestFailed,

        /// <summary>The provider is not configured for this app on the server.</summary>
        ProviderConfigNotFound,

        /// <summary>The provider's client credentials are not registered on the server.</summary>
        ProviderClientInfoNotExists,

        /// <summary>The service has been terminated for this app.</summary>
        ServiceTerminated,

        /// <summary>The client id is not one this app may use.</summary>
        InvalidClient,

        /// <summary>The caller's IP is blocked.</summary>
        IpBlocked,

        /// <summary>The app id is unknown to the server.</summary>
        AppNotFound,

        /// <summary>
        /// Token issuance is temporarily unavailable.
        /// </summary>
        TemporarilyUnavailable,

        /// <summary>The token endpoint does not support the requested grant type.</summary>
        UnsupportedGrantType,

        /// <summary>The authorization code was rejected.</summary>
        InvalidAuthorizationCode,

        /// <summary>The authorization code had already expired.</summary>
        ExpiredAuthorizationCode,

        /// <summary>The PKCE verifier did not match the challenge the login started with.</summary>
        CodeChallengeMismatch,

        /// <summary>The refresh token was rejected.</summary>
        InvalidRefreshToken,
    }
}

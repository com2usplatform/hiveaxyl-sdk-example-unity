// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// The device token the platform issued, with the provider it belongs to — or the failure that
    /// kept the platform from issuing one.
    /// </summary>
    public sealed class PushTokenResult
    {
        private PushTokenResult(string token, PushTokenProvider provider, HiveError error)
        {
            Token = token ?? string.Empty;
            Provider = provider;
            Error = error;
        }

        /// <summary>The platform answered with a token.</summary>
        public bool IsSuccess => Error == null;

        /// <summary>The device token, or empty on failure.</summary>
        public string Token { get; }

        /// <summary>
        /// Who the token belongs to. For Apple this is read from the build's own
        /// <c>aps-environment</c> entitlement, not assumed — a token registered under the wrong
        /// provider is silently undeliverable.
        /// </summary>
        public PushTokenProvider Provider { get; }

        /// <summary>The failure that kept the platform from issuing a token, or null.</summary>
        public HiveError Error { get; }

        /// <summary>The platform issued this token.</summary>
        public static PushTokenResult Issued(string token, PushTokenProvider provider) =>
            new PushTokenResult(token, provider, null);

        /// <summary>The platform could not issue a token; the error is preserved as given.</summary>
        public static PushTokenResult Failed(HiveError error) =>
            new PushTokenResult(string.Empty, default, error);
    }
}

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Builds the <c>openid.return_to</c> for Steam's OpenID 2.0 sign-in where the app's callback is
    /// a custom scheme. Steam refuses a non-http(s) <c>return_to</c> when it processes the login, so
    /// the URL Steam is given is the Axyl server's relay endpoint, carrying the app's own callback as
    /// <c>relayTo</c> and a per-attempt random value as <c>s</c>; the relay preserves the
    /// <c>openid.*</c> query and redirects to that scheme.
    /// </summary>
    /// <remarks>
    /// Nothing verifies <c>s</c> here: Steam echoes the whole <c>return_to</c> back inside the signed
    /// assertion, and <c>SteamOpenIdCredentialSource</c> already refuses a callback whose
    /// <c>openid.return_to</c> is not the URL the attempt started with — which is the same check a
    /// <c>state</c> comparison performs on OAuth.
    /// </remarks>
    public static class SteamRelayReturnTo
    {
        /// <summary>
        /// The relay URL to hand Steam, or <paramref name="relayBase"/> unchanged when
        /// <paramref name="appCallback"/> is blank — a platform that captures an http(s) callback
        /// itself (a Windows loopback, a WebGL callback page) needs no relay.
        /// </summary>
        /// <param name="relayBase">The relay endpoint, with or without a query of its own.</param>
        /// <param name="appCallback">The app-scheme URI the relay redirects to, or blank for none.</param>
        /// <param name="nonce">A fresh random value, one per sign-in attempt.</param>
        public static string Build(string relayBase, string appCallback, string nonce)
        {
            var relay = relayBase ?? string.Empty;
            if (string.IsNullOrWhiteSpace(appCallback))
            {
                return relay;
            }

            // The relay base is configuration and may already carry a query, in which case the
            // relay parameters have to be appended to it rather than start a second one.
            var separator = relay.IndexOf('?') >= 0 ? '&' : '?';
            return relay
                + separator
                + "relayTo=" + Uri.EscapeDataString(appCallback)
                + "&s=" + Uri.EscapeDataString(nonce ?? string.Empty);
        }
    }
}

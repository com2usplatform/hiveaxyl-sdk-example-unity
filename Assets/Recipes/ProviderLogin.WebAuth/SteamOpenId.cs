// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Linq;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// The pieces of Steam's OpenID 2.0 login that are pure string work: the URL that opens Steam's
    /// sign-in page, the SteamID inside the identity Steam hands back, and the assertion the server
    /// verifies.
    /// </summary>
    /// <remarks>
    /// Preserve the callback query string for verification. Do not re-encode its signed fields.
    /// </remarks>
    internal static class SteamOpenId
    {
        internal const string k_LoginEndpoint = "https://steamcommunity.com/openid/login";

        private const string k_Namespace = "http://specs.openid.net/auth/2.0";
        private const string k_IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";
        private const string k_ClaimedIdPrefix = "https://steamcommunity.com/openid/id/";

        /// <summary>
        /// The URL that opens Steam's sign-in page and sends the player back to
        /// <paramref name="returnTo"/> with the assertion in its query. The realm is that URL's
        /// origin, which is what Steam requires it to sit under and what the player is shown.
        /// </summary>
        /// <param name="returnTo">An absolute http(s) URL Steam can redirect to.</param>
        internal static string BuildLoginUrl(string returnTo)
        {
            if (!Uri.TryCreate(returnTo, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException(
                    "returnTo must be an absolute http or https URL.", nameof(returnTo));
            }

            var fields = new[]
            {
                ("openid.ns", k_Namespace),
                ("openid.mode", "checkid_setup"),
                ("openid.return_to", returnTo),
                ("openid.realm", uri.GetLeftPart(UriPartial.Authority)),
                ("openid.identity", k_IdentifierSelect),
                ("openid.claimed_id", k_IdentifierSelect),
            };
            return k_LoginEndpoint + "?" + string.Join("&",
                fields.Select(f => Uri.EscapeDataString(f.Item1) + "=" + Uri.EscapeDataString(f.Item2)));
        }

        /// <summary>
        /// Reads the SteamID out of the identity Steam asserted, which has the shape
        /// <c>https://steamcommunity.com/openid/id/&lt;steam_id64&gt;</c>. False for anything else —
        /// a different host, a non-numeric tail, or nothing at all.
        /// </summary>
        internal static bool TryParseSteamId(string claimedId, out string steamId64)
        {
            steamId64 = null;
            if (string.IsNullOrEmpty(claimedId)
                || !claimedId.StartsWith(k_ClaimedIdPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var tail = claimedId.Substring(k_ClaimedIdPrefix.Length);
            if (tail.Length == 0 || !ulong.TryParse(tail, out var id) || id == 0)
            {
                return false;
            }

            steamId64 = tail;
            return true;
        }

        /// <summary>
        /// The assertion as the server wants it: the callback's query string as Steam sent it, minus
        /// any fragment. Empty when the URL carries no query.
        /// </summary>
        internal static string ProviderTokenFrom(string callbackUrl)
        {
            var query = callbackUrl?.IndexOf('?') ?? -1;
            if (query < 0)
            {
                return string.Empty;
            }

            var raw = callbackUrl.Substring(query + 1);
            var fragment = raw.IndexOf('#');
            return fragment >= 0 ? raw.Substring(0, fragment) : raw;
        }
    }
}

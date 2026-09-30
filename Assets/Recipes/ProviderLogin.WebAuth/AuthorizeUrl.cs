// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Builds provider authorization URLs and generates callback state values.
    /// </summary>
    internal static class AuthorizeUrl
    {
        /// <summary>
        /// Returns a fresh, unguessable state value to correlate the callback with this attempt.
        /// </summary>
        internal static string NewState()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Assembles the authorization-code request URL.
        /// </summary>
        /// <param name="authorizeEndpoint">The provider's authorization endpoint.</param>
        /// <param name="clientId">The OAuth client id registered with the provider.</param>
        /// <param name="redirectUri">Where the provider sends the callback. Sent again at exchange
        /// time, where the provider requires the two to match.</param>
        /// <param name="scope">The scope string, omitted from the URL when empty.</param>
        /// <param name="state">The CSRF state to echo back.</param>
        /// <param name="codeChallenge">The PKCE S256 challenge for the provider's own exchange.</param>
        /// <param name="responseMode">Optional provider response mode, such as Apple form_post.</param>
        internal static string Build(
            string authorizeEndpoint,
            string clientId,
            string redirectUri,
            string scope,
            string state,
            string codeChallenge,
            string responseMode = null)
        {
            var sb = new StringBuilder();
            sb.Append(authorizeEndpoint ?? string.Empty).Append('?');
            Append(sb, "response_type", "code", first: true);
            Append(sb, "client_id", clientId, first: false);
            Append(sb, "redirect_uri", redirectUri, first: false);

            if (!string.IsNullOrEmpty(scope))
            {
                Append(sb, "scope", scope, first: false);
            }

            Append(sb, "state", state, first: false);
            if (!string.IsNullOrEmpty(responseMode))
            {
                Append(sb, "response_mode", responseMode, first: false);
            }

            if (!string.IsNullOrEmpty(codeChallenge))
            {
                Append(sb, "code_challenge", codeChallenge, first: false);
                Append(sb, "code_challenge_method", "S256", first: false);
            }

            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string key, string value, bool first)
        {
            if (!first)
            {
                sb.Append('&');
            }

            sb.Append(Uri.EscapeDataString(key))
                .Append('=')
                .Append(Uri.EscapeDataString(value ?? string.Empty));
        }
    }
}

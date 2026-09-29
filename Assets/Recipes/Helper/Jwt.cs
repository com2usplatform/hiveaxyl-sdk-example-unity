// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Text;
using System.Text.RegularExpressions;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Reads JWT claims without verifying the signature. Expiry values are scheduling hints only;
    /// this helper does not establish that a token is authentic.
    /// </summary>
    public static class Jwt
    {
        /// <summary>
        /// Reads the <c>exp</c> claim as seconds since the Unix epoch. Returns 0 when the token is
        /// absent, malformed, or carries no <c>exp</c> — callers decide what an unknown expiry means.
        /// </summary>
        public static long ReadExp(string jwt)
        {
            if (string.IsNullOrEmpty(jwt))
            {
                return 0;
            }

            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return 0;
            }

            string payload;
            try
            {
                payload = Encoding.UTF8.GetString(Base64Url.Decode(parts[1]));
            }
            catch
            {
                return 0;
            }

            var match = Regex.Match(payload, "\"exp\"\\s*:\\s*(\\d+)");
            if (match.Success && long.TryParse(match.Groups[1].Value, out var exp))
            {
                return exp;
            }

            return 0;
        }

        /// <summary>
        /// Reads a string claim (e.g. <c>sub</c>) from the JWT payload. No signature verification.
        /// Returns empty string on any failure.
        /// </summary>
        public static string ReadStringClaim(string jwt, string claim)
        {
            if (string.IsNullOrEmpty(jwt) || string.IsNullOrEmpty(claim))
            {
                return string.Empty;
            }

            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return string.Empty;
            }

            string payload;
            try
            {
                payload = Encoding.UTF8.GetString(Base64Url.Decode(parts[1]));
            }
            catch
            {
                return string.Empty;
            }

            var match = Regex.Match(payload, "\"" + Regex.Escape(claim) + "\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }
    }
}

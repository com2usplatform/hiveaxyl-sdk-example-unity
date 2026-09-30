// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Text;
using Newtonsoft.Json.Linq;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Reads the well-known fields the provider token exchange needs from an OAuth 2.0 token
    /// response and from the id_token it carries: the top-level <c>id_token</c>/<c>access_token</c>,
    /// and either the id_token's <c>sub</c> claim or the userinfo response's <c>data.id</c> (X).
    /// </summary>
    internal static class OAuthJson
    {
        /// <summary>Reads a top-level string field, e.g. <c>id_token</c>; false if absent or not a string.</summary>
        public static bool TryReadStringField(string json, string key, out string value)
        {
            value = string.Empty;
            if (Parse(json)?[key] is JValue jv && jv.Type == JTokenType.String)
            {
                value = jv.Value<string>();
                return true;
            }

            return false;
        }

        /// <summary>Reads a string nested one object deep, e.g. <c>data.id</c> from the X userinfo response.</summary>
        public static bool TryReadObjectStringField(string json, string objectKey, string fieldKey, out string value)
        {
            value = string.Empty;
            if (Parse(json)?[objectKey] is JObject obj
                && obj[fieldKey] is JValue jv
                && jv.Type == JTokenType.String)
            {
                value = jv.Value<string>();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reads the provider user ID from the JWT payload without verifying its signature.
        /// The token still requires server verification.
        /// </summary>
        public static bool TryReadIdTokenSubject(string idToken, out string subject)
        {
            subject = string.Empty;
            if (string.IsNullOrEmpty(idToken))
            {
                return false;
            }

            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return false;
            }

            string payloadJson;
            try
            {
                payloadJson = Encoding.UTF8.GetString(Base64Url.Decode(parts[1]));
            }
            catch
            {
                return false;
            }

            return TryReadStringField(payloadJson, "sub", out subject);
        }

        private static JObject Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }
    }
}

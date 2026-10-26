// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using Hive.Axyl.Auth.Addon.Apple;
using Hive.Axyl.Core;
using Hive.Axyl.Core.Serialization;

namespace Hive.Axyl.Samples.Recipes
{
    // Apple's external protocol uses its own keys, independently of the Axyl bridge wire format.
    internal static class AppleWebSignInParser
    {
        internal static AppleSignInServiceLoginResult Parse(
            string json, string expectedState, out AppleWebGlProfile profile)
        {
            profile = null;
            try
            {
                var root = ReadObject(json);
                if (root == null)
                {
                    return Failure(HiveErrorCode.Internal, "Malformed Apple response.", json);
                }
                var error = String(root, "error");
                if (error.Length != 0)
                {
                    return error == "popup_closed_by_user" || error == "user_cancelled_authorize"
                        ? new AppleSignInServiceLoginResult.UserCanceled(json)
                        : Failure(HiveErrorCode.Internal, "Apple authorization failed: " + error, json);
                }
                var auth = Object(root, "authorization");
                if (auth == null)
                {
                    return Failure(HiveErrorCode.Internal, "Missing Apple authorization.", json);
                }
                if (expectedState.Length == 0 || String(auth, "state") != expectedState)
                {
                    return Failure(HiveErrorCode.PermissionDenied, "Apple authorization state mismatch.", json);
                }
                return ReadCredential(root, auth, json, out profile);
            }
            catch (JsonParseException)
            {
                return Failure(HiveErrorCode.Internal, "Malformed Apple response.", json);
            }
        }

        private static AppleSignInServiceLoginResult ReadCredential(
            Dictionary<string, (JsonTokenType Type, string Value)> root,
            Dictionary<string, (JsonTokenType Type, string Value)> auth, string json,
            out AppleWebGlProfile profile)
        {
            profile = null;
            var code = String(auth, "code");
            var token = String(auth, "id_token");
            if (code.Length == 0 || token.Length == 0)
            {
                return Failure(HiveErrorCode.Internal, "Missing Apple code or identity token.", json);
            }
            var user = Object(root, "user");
            var name = Object(user, "name");
            var email = String(user, "email");
            var claims = TokenClaims(token);
            profile = new AppleWebGlProfile(String(name, "firstName"), String(name, "lastName"),
                email.Length == 0 ? String(claims, "email") : email, user != null);
            return new AppleSignInServiceLoginResult.Success(new AppleSignInServiceLoginResponse
            {
                AuthorizationCode = code,
                IdentityToken = token,
                UserIdentifier = String(claims, "sub"),
                Email = email,
                UserName = new AppleUserName { GivenName = profile.FirstName, FamilyName = profile.LastName },
                RealUserStatus = RealUserStatus.Unsupported,
            }, json);
        }

        private static AppleSignInServiceLoginResult Failure(HiveErrorCode code, string message, string json) =>
            new AppleSignInServiceLoginResult.Failure(new HiveError(code, message), json);

        private static Dictionary<string, (JsonTokenType Type, string Value)> TokenClaims(string token)
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            // JWT uses unpadded Base64URL; Base64 decoding requires padding to a multiple of four.
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            try
            {
                return ReadObject(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            }
            catch (FormatException)
            {
                return null;
            }
            catch (JsonParseException)
            {
                return null;
            }
        }

        private static string String(Dictionary<string, (JsonTokenType Type, string Value)> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var value) && value.Type == JsonTokenType.String
                ? value.Value : string.Empty;

        private static Dictionary<string, (JsonTokenType Type, string Value)> Object(
            Dictionary<string, (JsonTokenType Type, string Value)> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var value) && value.Type == JsonTokenType.StartObject
                ? ReadObject(value.Value) : null;

        private static Dictionary<string, (JsonTokenType Type, string Value)> ReadObject(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            var reader = new JsonReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }
            var fields = new Dictionary<string, (JsonTokenType, string)>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return null;
                }
                var key = reader.ReadString();
                if (!reader.Read())
                {
                    return null;
                }
                var type = reader.TokenType;
                if (type == JsonTokenType.EndObject || type == JsonTokenType.EndArray
                    || type == JsonTokenType.PropertyName || type == JsonTokenType.None)
                {
                    return null;
                }
                var value = type == JsonTokenType.String ? reader.ReadString() : reader.ReadRaw();
                fields[key] = (type, value);
            }
            return reader.TokenType == JsonTokenType.EndObject && !reader.Read() ? fields : null;
        }
    }
}

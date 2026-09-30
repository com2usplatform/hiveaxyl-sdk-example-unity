// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Maps a third-party provider's OAuth 2.0 token-endpoint error to a canonical
    /// <see cref="HiveErrorCode"/> using RFC 6749 §5.2. Provider errors use the OAuth format,
    /// not the Axyl server response format.
    /// </summary>
    internal static class OAuthErrorMap
    {
        /// <summary>
        /// Maps the OAuth token-endpoint <c>error</c> code (RFC 6749 §5.2) to a canonical code; an
        /// unrecognized or empty code falls back to <see cref="ForStatus"/>.
        /// </summary>
        internal static HiveErrorCode ForOAuthError(string error, long status)
        {
            switch (error)
            {
                case "invalid_request":
                case "invalid_grant":
                case "unsupported_grant_type":
                case "invalid_scope":
                    return HiveErrorCode.InvalidArgument;
                case "invalid_client":
                case "unauthorized_client":
                    return HiveErrorCode.Unauthenticated;
                default:
                    return ForStatus(status);
            }
        }

        /// <summary>HTTP-status fallback for a response without a usable OAuth error body.</summary>
        internal static HiveErrorCode ForStatus(long status)
        {
            switch (status)
            {
                case 400: return HiveErrorCode.InvalidArgument;
                case 401: return HiveErrorCode.Unauthenticated;
                default: return status >= 500 ? HiveErrorCode.Unavailable : HiveErrorCode.Internal;
            }
        }
    }
}

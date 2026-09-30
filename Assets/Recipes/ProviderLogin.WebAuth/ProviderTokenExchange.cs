// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using UnityEngine.Networking;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Exchanges authorization codes directly with public OAuth providers using PKCE.
    /// Requests do not include Axyl session or identity headers.
    /// </summary>
    public sealed class ProviderTokenExchange
    {
        // A provider host that accepts the TCP connection but never responds would otherwise hang the
        // exchange forever (UnityWebRequest.timeout defaults to 0 = no limit), and a caller
        // that passes no cancellation token would wait with it. Bound every request.
        private const int k_RequestTimeoutSeconds = 30;

        /// <summary>
        /// Exchanges a Google authorization code for its id_token at <paramref name="tokenEndpoint"/>
        /// (typically <c>https://oauth2.googleapis.com/token</c>) and returns the id_token with its
        /// <c>sub</c> claim as the provider user id. Google returns an OIDC id_token because the
        /// authorize request carried the <c>openid</c> scope; no <c>client_secret</c> is sent (native
        /// public client, PKCE).
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when any required argument is null or empty.</exception>
        public async Task<ProviderTokenExchangeResult> ExchangeGoogleAsync(
            string tokenEndpoint,
            string clientId,
            string authorizationCode,
            string codeVerifier,
            string redirectUri,
            CancellationToken ct = default)
        {
            RequireNonEmpty(tokenEndpoint, nameof(tokenEndpoint));
            RequireNonEmpty(clientId, nameof(clientId));
            RequireNonEmpty(authorizationCode, nameof(authorizationCode));
            RequireNonEmpty(codeVerifier, nameof(codeVerifier));
            RequireNonEmpty(redirectUri, nameof(redirectUri));

            var form = AuthorizationCodeForm(clientId, authorizationCode, codeVerifier, redirectUri);
            using (var request = NewFormPost(tokenEndpoint, form))
            {
                var outcome = await SendAsync(request, ct);
                if (!outcome.IsSuccess)
                {
                    return outcome.Failure;
                }

                if (!OAuthJson.TryReadStringField(outcome.Body, "id_token", out var idToken) || idToken.Length == 0)
                {
                    return Fail(HiveErrorCode.Internal, "Google token response had no id_token.");
                }

                if (!OAuthJson.TryReadIdTokenSubject(idToken, out var subject) || subject.Length == 0)
                {
                    return Fail(HiveErrorCode.Internal, "Google id_token had no sub claim.");
                }

                return ProviderTokenExchangeResult.Ok(subject, idToken);
            }
        }

        /// <summary>
        /// Exchanges an X (Twitter) authorization code for an access token at
        /// <paramref name="tokenEndpoint"/>, then reads the user id from
        /// <paramref name="userInfoEndpoint"/> (e.g. <c>https://api.x.com/2/users/me</c>). X is OAuth
        /// 2.0, not OIDC: its token endpoint returns an access_token with no embedded user id, so a
        /// second, bearer-authenticated call is required. No <c>client_secret</c> is sent (public
        /// client, PKCE).
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when any required argument is null or empty.</exception>
        public async Task<ProviderTokenExchangeResult> ExchangeXAsync(
            string tokenEndpoint,
            string userInfoEndpoint,
            string clientId,
            string authorizationCode,
            string codeVerifier,
            string redirectUri,
            CancellationToken ct = default)
        {
            RequireNonEmpty(tokenEndpoint, nameof(tokenEndpoint));
            RequireNonEmpty(userInfoEndpoint, nameof(userInfoEndpoint));
            RequireNonEmpty(clientId, nameof(clientId));
            RequireNonEmpty(authorizationCode, nameof(authorizationCode));
            RequireNonEmpty(codeVerifier, nameof(codeVerifier));
            RequireNonEmpty(redirectUri, nameof(redirectUri));

            string accessToken;
            var form = AuthorizationCodeForm(clientId, authorizationCode, codeVerifier, redirectUri);
            using (var tokenRequest = NewFormPost(tokenEndpoint, form))
            {
                var tokenOutcome = await SendAsync(tokenRequest, ct);
                if (!tokenOutcome.IsSuccess)
                {
                    return tokenOutcome.Failure;
                }

                if (!OAuthJson.TryReadStringField(tokenOutcome.Body, "access_token", out accessToken)
                    || accessToken.Length == 0)
                {
                    return Fail(HiveErrorCode.Internal, "X token response had no access_token.");
                }
            }

            // An X access token carries no user id, so read it from the userinfo endpoint (v2 wraps
            // the user in a `data` object) using the access token as the bearer.
            using (var meRequest = NewBearerGet(userInfoEndpoint, accessToken))
            {
                var meOutcome = await SendAsync(meRequest, ct);
                if (!meOutcome.IsSuccess)
                {
                    return meOutcome.Failure;
                }

                if (!OAuthJson.TryReadObjectStringField(meOutcome.Body, "data", "id", out var userId)
                    || userId.Length == 0)
                {
                    return Fail(HiveErrorCode.Internal, "X userinfo response had no data.id.");
                }

                return ProviderTokenExchangeResult.Ok(userId, accessToken);
            }
        }

        private static string AuthorizationCodeForm(
            string clientId, string authorizationCode, string codeVerifier, string redirectUri)
            => new StringBuilder()
                .Append("grant_type=authorization_code")
                .Append("&code=").Append(Uri.EscapeDataString(authorizationCode))
                .Append("&code_verifier=").Append(Uri.EscapeDataString(codeVerifier))
                .Append("&client_id=").Append(Uri.EscapeDataString(clientId))
                .Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri))
                .ToString();

        private static UnityWebRequest NewFormPost(string url, string form)
        {
            var request = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(form)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = k_RequestTimeoutSeconds,
            };
            request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            return request;
        }

        private static UnityWebRequest NewBearerGet(string url, string bearerToken)
        {
            var request = UnityWebRequest.Get(url);
            request.timeout = k_RequestTimeoutSeconds;
            request.SetRequestHeader("Authorization", "Bearer " + bearerToken);
            return request;
        }

        // Sends the request and returns the response body, or a typed failure: caller cancellation ->
        // Cancelled, a transport error (no HTTP response) -> Unavailable, and a non-2xx response ->
        // its status-mapped code. UnityWebRequest.SendWebRequest completes on the Unity main thread,
        // and the await keeps that context, so the response is read on the main thread.
        private static async Task<SendOutcome> SendAsync(UnityWebRequest request, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            operation.completed += _ => completion.TrySetResult(true);

            using (ct.Register(() => request.Abort()))
            {
                await completion.Task;
            }

            if (ct.IsCancellationRequested)
            {
                return SendOutcome.Fail(Fail(HiveErrorCode.Cancelled, "Provider token exchange cancelled."));
            }

            if (request.result == UnityWebRequest.Result.ConnectionError
                || request.result == UnityWebRequest.Result.DataProcessingError)
            {
                return SendOutcome.Fail(Fail(HiveErrorCode.Unavailable, $"Provider request failed: {request.error}"));
            }

            var status = request.responseCode;
            var body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (status < 200 || status >= 300)
            {
                return SendOutcome.Fail(FailFromOAuthError(status, body));
            }

            return SendOutcome.Success(body);
        }

        // Parse provider errors using the OAuth error response format.
        private static ProviderTokenExchangeResult FailFromOAuthError(long status, string body)
        {
            OAuthJson.TryReadStringField(body, "error", out var error);
            OAuthJson.TryReadStringField(body, "error_description", out var description);

            var detail = error.Length == 0
                ? $"HTTP {status}"
                : description.Length == 0 ? error : $"{error}: {description}";
            return Fail(OAuthErrorMap.ForOAuthError(error, status),
                $"Provider token exchange failed ({detail}).");
        }

        private static ProviderTokenExchangeResult Fail(HiveErrorCode code, string message)
            => ProviderTokenExchangeResult.Fail(new HiveError(code, message));

        private static void RequireNonEmpty(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Value must not be null or empty.", name);
            }
        }

        // Either a successful body or a typed failure result, so the callers branch on one value.
        private readonly struct SendOutcome
        {
            private SendOutcome(bool ok, string body, ProviderTokenExchangeResult failure)
            {
                IsSuccess = ok;
                Body = body;
                Failure = failure;
            }

            public bool IsSuccess { get; }

            public string Body { get; }

            public ProviderTokenExchangeResult Failure { get; }

            public static SendOutcome Success(string body) => new SendOutcome(true, body, null);

            public static SendOutcome Fail(ProviderTokenExchangeResult failure)
                => new SendOutcome(false, string.Empty, failure);
        }
    }
}

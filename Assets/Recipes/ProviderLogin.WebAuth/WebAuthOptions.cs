// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using UnityEngine;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// The OAuth client registration a browser sign-in runs under. Every value here is app
    /// configuration — the Recipes hold no provider endpoints of their own.
    /// </summary>
    public sealed class WebAuthOptions
    {
        private const string k_WebGlCallbackPage = "callback.html";

        private WebAuthOptions(
            string authorizeEndpoint,
            string clientId,
            string scope,
            string redirectUri,
            string loopbackPathPrefix,
            string tokenEndpoint,
            string userInfoEndpoint)
        {
            AuthorizeEndpoint = authorizeEndpoint;
            ClientId = clientId;
            Scope = scope;
            RedirectUri = redirectUri;
            LoopbackPathPrefix = loopbackPathPrefix;
            TokenEndpoint = tokenEndpoint ?? string.Empty;
            UserInfoEndpoint = userInfoEndpoint ?? string.Empty;
        }

        /// <summary>The provider's authorization endpoint.</summary>
        public string AuthorizeEndpoint { get; }

        /// <summary>
        /// The OAuth client id registered with the provider. This is the provider's client id, not
        /// the Axyl PKCE client id the Recipe was constructed with.
        /// </summary>
        public string ClientId { get; }

        /// <summary>The scope string to request, or empty to request none.</summary>
        public string Scope { get; }

        /// <summary>
        /// The redirect URI registered with the provider, or empty when the redirect is a loopback
        /// address allocated at call time.
        /// </summary>
        public string RedirectUri { get; }

        /// <summary>
        /// The path a freshly allocated loopback redirect should listen on, or empty when
        /// <see cref="RedirectUri"/> is fixed.
        /// </summary>
        public string LoopbackPathPrefix { get; }

        /// <summary>Whether this configuration allocates its redirect URI instead of carrying one.</summary>
        public bool UsesLoopbackRedirect => string.IsNullOrEmpty(RedirectUri);

        /// <summary>
        /// The WebGL callback-page URI. Ship <c>Assets/StreamingAssets/callback.html</c>
        /// with the player and register its deployed URL with the provider. Used only on WebGL.
        /// </summary>
        public static string WebGlCallbackUri() => Application.streamingAssetsPath + "/" + k_WebGlCallbackPage;

        /// <summary>
        /// The provider's OAuth token endpoint. Set for a public client (no <c>client_secret</c>
        /// issued) whose authorization code the client exchanges itself (PKCE), rather than having
        /// the server exchange it; empty for a confidential client the server handles.
        /// </summary>
        public string TokenEndpoint { get; }

        /// <summary>
        /// The provider's userinfo endpoint, used only when the token endpoint returns no id_token
        /// and the user id must be fetched separately (X returns an access_token, so the id comes
        /// from <c>/2/users/me</c>). Empty otherwise.
        /// </summary>
        public string UserInfoEndpoint { get; }

        /// <summary>
        /// Whether a configured <see cref="TokenEndpoint"/> enables direct provider token exchange.
        /// </summary>
        public bool ClientSideExchange => !string.IsNullOrEmpty(TokenEndpoint);

        /// <summary>
        /// Signs in against a redirect URI the provider already has registered — a custom scheme on
        /// mobile, or a hosted relay.
        /// </summary>
        /// <param name="authorizeEndpoint">The provider's authorization endpoint. Must not be blank.</param>
        /// <param name="clientId">The OAuth client id registered with the provider. Must not be blank.</param>
        /// <param name="redirectUri">The registered redirect URI. Must not be blank.</param>
        /// <param name="scope">The scope string to request. May be null or empty.</param>
        /// <exception cref="ArgumentException">Thrown when a required argument is blank.</exception>
        public static WebAuthOptions Create(
            string authorizeEndpoint, string clientId, string redirectUri, string scope = null,
            string tokenEndpoint = null, string userInfoEndpoint = null)
        {
            RequireText(authorizeEndpoint, nameof(authorizeEndpoint));
            RequireText(clientId, nameof(clientId));
            RequireText(redirectUri, nameof(redirectUri));

            return new WebAuthOptions(
                authorizeEndpoint, clientId, scope ?? string.Empty, redirectUri, string.Empty,
                tokenEndpoint, userInfoEndpoint);
        }

        /// <summary>
        /// Signs in against a loopback redirect allocated when the login runs — the desktop route,
        /// where the port is not known until a listener is bound.
        /// </summary>
        /// <param name="authorizeEndpoint">The provider's authorization endpoint. Must not be blank.</param>
        /// <param name="clientId">The OAuth client id registered with the provider. Must not be blank.</param>
        /// <param name="loopbackPathPrefix">The path the listener answers on. Must not be blank.</param>
        /// <param name="scope">The scope string to request. May be null or empty.</param>
        /// <exception cref="ArgumentException">Thrown when a required argument is blank.</exception>
        public static WebAuthOptions WithLoopbackRedirect(
            string authorizeEndpoint, string clientId, string loopbackPathPrefix, string scope = null,
            string tokenEndpoint = null, string userInfoEndpoint = null)
        {
            RequireText(authorizeEndpoint, nameof(authorizeEndpoint));
            RequireText(clientId, nameof(clientId));
            RequireText(loopbackPathPrefix, nameof(loopbackPathPrefix));

            return new WebAuthOptions(
                authorizeEndpoint, clientId, scope ?? string.Empty, string.Empty, loopbackPathPrefix,
                tokenEndpoint, userInfoEndpoint);
        }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    $"{name} must be a non-empty, non-whitespace string.", name);
            }
        }
    }
}

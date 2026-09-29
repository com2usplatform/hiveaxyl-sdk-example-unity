// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
// The credential-source interface exposes a `LoginProvider Provider` property, which
// shadows the Auth `Provider` enum inside these classes — the alias keeps the enum reachable.
using AuthProvider = Hive.Axyl.Auth.Provider;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Signs in with X through the browser. Requires the WebAuth addon.
    /// </summary>
    public sealed class XCredentialSource : IProviderCredentialSource
    {
        private readonly WebAuthOptions m_options;

        /// <summary>Creates the source.</summary>
        /// <param name="options">X's OAuth client registration. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public XCredentialSource(WebAuthOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc />
        public LoginProvider Provider => LoginProvider.X;

        /// <inheritdoc />
        public Task<ProviderCredentialOutcome> AcquireAsync(
            CancellationToken cancellationToken = default) =>
            WebAuthCredentialFlow.AcquireAsync(
                LoginProvider.X, AuthProvider.X, m_options, cancellationToken);
    }

    /// <summary>
    /// Signs in with Google through the browser. Requires the WebAuth addon.
    /// </summary>
    /// <remarks>
    /// This is Google's browser route. Signing in with Google through Android's Credential Manager
    /// is a different addon and a different source — <c>GoogleCredentialManagerCredentialSource</c>
    /// in <c>ProviderLogin.CredentialManager/</c>.
    /// </remarks>
    public sealed class GoogleCredentialSource : IProviderCredentialSource
    {
        private readonly WebAuthOptions m_options;

        /// <summary>Creates the source.</summary>
        /// <param name="options">Google's OAuth client registration. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public GoogleCredentialSource(WebAuthOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc />
        public LoginProvider Provider => LoginProvider.Google;

        /// <inheritdoc />
        public Task<ProviderCredentialOutcome> AcquireAsync(
            CancellationToken cancellationToken = default) =>
            WebAuthCredentialFlow.AcquireAsync(
                LoginProvider.Google, AuthProvider.Google, m_options, cancellationToken);
    }

    /// <summary>
    /// Signs in with Apple through an HTTPS callback relay and exchanges the code on the server.
    /// The relay must accept form_post and forward the code and unprefixed state to the app.
    /// </summary>
    public sealed class AppleWebCredentialSource : IProviderCredentialSource
    {
        private readonly WebAuthOptions m_options;
        private readonly string m_callbackUri;

        /// <summary>Creates Apple's browser route using a registered Services ID.</summary>
        /// <param name="options">Apple registration with an HTTPS relay and server-side exchange.</param>
        /// <param name="callbackUri">App callback forwarded by the relay. Omit for Windows loopback.</param>
        public AppleWebCredentialSource(WebAuthOptions options, string callbackUri = null)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            if (!Uri.TryCreate(options.RedirectUri, UriKind.Absolute, out var redirect)
                || redirect.Scheme != Uri.UriSchemeHttps || redirect.IsLoopback
                || !string.IsNullOrEmpty(redirect.Fragment) || options.ClientSideExchange)
            {
                throw new ArgumentException(
                    "Apple requires a registered HTTPS relay and server-side code exchange.", nameof(options));
            }

            if (callbackUri != null && (!Uri.TryCreate(callbackUri, UriKind.Absolute, out var callback)
                || callbackUri.Contains("|") || !string.IsNullOrEmpty(callback.Fragment)))
            {
                throw new ArgumentException("The relay callback must be an absolute URI without a fragment or '|'.",
                    nameof(callbackUri));
            }

            m_callbackUri = callbackUri;
        }

        /// <inheritdoc />
        public LoginProvider Provider => LoginProvider.Apple;

        /// <inheritdoc />
        public Task<ProviderCredentialOutcome> AcquireAsync(CancellationToken cancellationToken = default) =>
            WebAuthCredentialFlow.AcquireAsync(
                LoginProvider.Apple, AuthProvider.SigninApple, m_options, cancellationToken, m_callbackUri);
    }

}

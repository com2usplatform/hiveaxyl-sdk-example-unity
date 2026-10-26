// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth.Addon.Apple;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>Signs in with Apple in a Unity WebGL player using Apple's JavaScript popup.</summary>
    /// <remarks>
    /// Call Preload when the sign-in screen opens, then LoginAsync from the player's click
    /// without awaiting other work first. Register the page's domain and HTTPS redirect URI
    /// on the Apple Services ID. This helper does not exchange codes or install an Axyl session.
    /// All calls must originate on Unity's main thread. A newer browser login cancels the
    /// pending browser login, including one started by another instance. Cancellation stops
    /// waiting; it does not close Apple's window. Late results are ignored.
    /// Apple's browser SDK replaces an active popup when signIn is called again; canceling the
    /// previous managed request follows that behavior rather than retaining a superseded session.
    /// </remarks>
    internal sealed class AppleWebAuthorization
    {
        private readonly bool m_isSupported = true;
        private readonly Func<string, string, string, string, string, CancellationToken, Task<string>> m_signIn;

        /// <summary>Creates the browser sign-in plugin. Off WebGL, LoginAsync returns Unimplemented.</summary>
        public AppleWebAuthorization() : this(AppleWebPopupBridge.SignInAsync)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            m_isSupported = false;
#endif
        }

        internal AppleWebAuthorization(
            Func<string, string, string, string, string, CancellationToken, Task<string>> signIn)
        {
            m_signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        }

        /// <summary>
        /// Profile from the last successful Apple authorization, or null. Available before any
        /// server exchange, and retained if a later attempt fails. Not proof of account identity.
        /// </summary>
        public AppleWebGlProfile Profile { get; private set; }

        /// <summary>Loads Apple's script ahead of the click. Idempotent; no-op outside WebGL.</summary>
        public static void Preload() => AppleWebPopupBridge.Preload();

        /// <summary>Authorizes with Apple and returns the code and identity token for backend verification.</summary>
        /// <param name="clientId">The registered Apple Services ID.</param>
        /// <param name="redirectUri">An HTTPS return URL registered on the Services ID.</param>
        /// <param name="scope">Apple scopes: name and/or email, separated by spaces; empty requests neither.</param>
        /// <param name="cancellationToken">Stops waiting with Failure(Cancelled); distinct from dismissal.</param>
        /// <returns>
        /// Success with Apple's credential, UserCanceled, or Failure. Data.Email is from the
        /// one-time user object; Profile.Email also falls back to the unverified identity token.
        /// RealUserStatus is Unsupported in the browser. RawResponse contains sensitive Apple data.
        /// </returns>
        /// <exception cref="ArgumentException">The Services ID, HTTPS URI, or scope is invalid.</exception>
        public async Task<AppleSignInServiceLoginResult> LoginAsync(
            string clientId, string redirectUri, string scope = "name email",
            CancellationToken cancellationToken = default)
        {
            Validate(clientId, redirectUri, scope);
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled();
            }

            if (!m_isSupported)
            {
                return new AppleSignInServiceLoginResult.Failure(new HiveError(
                    HiveErrorCode.Unimplemented, "Apple browser sign-in is not supported outside a WebGL player."));
            }

            var state = Nonce.New();
            // Match the native Recipe: send a hash of a fresh nonce. Code exchange has no raw-nonce field.
            var nonceHash = Pkce.Sha256Hex(Nonce.New());
            string json;
            try
            {
                // Invoke before the first await to preserve browser user activation.
                json = await m_signIn(clientId, scope, redirectUri, state, nonceHash, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Canceled();
            }

            if (json == null)
            {
                return new AppleSignInServiceLoginResult.Failure(new HiveError(
                    HiveErrorCode.Unavailable,
                    "Apple browser sign-in requires a loaded Apple script. Call Preload before signing in."));
            }

            var result = AppleWebSignInParser.Parse(json, state, out var profile);
            if (result.IsSuccess)
            {
                Profile = profile;
            }
            return result;
        }

        private static AppleSignInServiceLoginResult Canceled() =>
            new AppleSignInServiceLoginResult.Failure(new HiveError(
                HiveErrorCode.Cancelled, "Apple browser sign-in was canceled by the caller or a newer request."));

        private static void Validate(string clientId, string redirectUri, string scope)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new ArgumentException("An Apple Services ID is required.", nameof(clientId));
            }
            if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || uri.IsLoopback
                || uri.HostNameType != UriHostNameType.Dns || uri.Fragment.Length != 0
                || uri.UserInfo.Length != 0)
            {
                throw new ArgumentException(
                    "Apple popup sign-in requires an HTTPS domain return URL. "
                    + "Run the game on an HTTPS domain and register its return URL with Apple. "
                    + "HTTP, localhost, and IP addresses are not supported.", nameof(redirectUri));
            }
            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }
            foreach (var value in scope.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (value != "name" && value != "email")
                {
                    throw new ArgumentException("Apple scopes must be name and/or email.", nameof(scope));
                }
            }
        }
    }
}

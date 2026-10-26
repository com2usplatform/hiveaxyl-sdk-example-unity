// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Auth.Addon.Apple;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>Asks the Apple JS popup to authorize in WebGL, then exchanges its code on the server.</summary>
    /// <remarks>
    /// This implementation requires a Unity WebGL player; other platforms return Unimplemented.
    /// Call AppleJsPopupCredentialSource.Preload when the screen opens. AcquireAsync must start from
    /// the player's click. Register the HTTPS return URL and page domain on the Apple Services ID.
    /// </remarks>
    public sealed class AppleJsPopupCredentialSource : IProviderCredentialSource
    {
        private readonly AppleWebAuthorization m_apple = new AppleWebAuthorization();
        private readonly Func<string, string, string, CancellationToken, Task<AppleSignInServiceLoginResult>>
            m_authorize;
        private readonly string m_clientId;
        private readonly string m_redirectUri;
        private readonly string m_scope;

        /// <summary>Creates the source; registration is validated when authorization starts.</summary>
        public AppleJsPopupCredentialSource(string clientId, string redirectUri, string scope = "name email")
        {
            m_authorize = m_apple.LoginAsync;
            m_clientId = clientId;
            m_redirectUri = redirectUri;
            m_scope = scope ?? string.Empty;
        }

        internal AppleJsPopupCredentialSource(
            string clientId, string redirectUri, string scope,
            Func<string, string, string, CancellationToken, Task<AppleSignInServiceLoginResult>> authorize)
            : this(clientId, redirectUri, scope)
        {
            m_authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));
        }

        /// <summary>Loads Apple JS before the player clicks sign-in. Safe to call more than once.</summary>
        public static void Preload() => AppleWebAuthorization.Preload();

        public LoginProvider Provider => LoginProvider.Apple;

        /// <summary>
        /// Profile from Apple authorization, available even if server exchange fails. The app must
        /// associate it with a verified account before persisting it; it is not proof of identity.
        /// </summary>
        public AppleWebGlProfile Profile => m_apple.Profile;

        public async Task<ProviderCredentialOutcome> AcquireAsync(CancellationToken cancellationToken = default)
        {
            if (!HiveCore.TryResolve<IAuthService>(out var auth) || auth == null)
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition, "Initialize the SDK with AddAuth before signing in."));
            }

            var authorized = await m_authorize(m_clientId, m_redirectUri, m_scope, cancellationToken);
            switch (authorized)
            {
                case AppleSignInServiceLoginResult.Success ok:
                    return await ProviderCodeExchange.ExchangeAsync(
                        auth, LoginProvider.Apple, Hive.Axyl.Auth.Provider.SigninApple,
                        ok.Data.AuthorizationCode, m_redirectUri, null, cancellationToken);
                case AppleSignInServiceLoginResult.UserCanceled _:
                    return ProviderCredentialOutcome.Canceled();
                case AppleSignInServiceLoginResult.Failure failure:
                    return ProviderCredentialOutcome.Failed(failure.Problem);
                case AppleSignInServiceLoginResult.UnknownOutcome unknown:
                    return ProviderCredentialOutcome.Unrecognized(unknown.Code, unknown.RawJson);
                default:
                    // Preserve an outcome introduced by a newer SDK without treating it as a technical failure.
                    return ProviderCredentialOutcome.Unrecognized(string.Empty, authorized.RawResponse);
            }
        }
    }
}

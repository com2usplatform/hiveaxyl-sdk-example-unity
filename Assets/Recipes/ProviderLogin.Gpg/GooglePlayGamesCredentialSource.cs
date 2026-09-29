// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Auth.Addon.GPG;
using Hive.Axyl.Core;
// The credential-source interface exposes a `LoginProvider Provider` property, which
// shadows the Auth `Provider` enum inside these classes — the alias keeps the enum reachable.
using AuthProvider = Hive.Axyl.Auth.Provider;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Signs in with Google Play Games and hands back the provider token the Axyl server issued for
    /// the server-side authorization code. Needs the Play Games addon; without it this source fails
    /// with <see cref="HiveErrorCode.Unavailable"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three native steps stand behind one credential: ask whether a player is already signed in,
    /// sign in if not, then request the server-side authorization code. Like the browser route, that
    /// code is exchanged by the server, so no Google client secret reaches the app.
    /// </para>
    /// </remarks>
    public sealed class GooglePlayGamesCredentialSource : IProviderCredentialSource
    {
        private readonly string m_webClientId;
        private readonly bool m_forceRefreshToken;

        /// <summary>
        /// Creates the source.
        /// </summary>
        /// <param name="webClientId">
        /// The "Web application" OAuth client id from the Google Cloud Console — the one the backend
        /// exchanges the returned code against. Must not be null or whitespace.
        /// </param>
        /// <param name="forceRefreshToken">
        /// Ask Play Games for a code that yields a refresh token. Costs the player a consent prompt,
        /// so leave it off unless the backend needs offline access.
        /// </param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="webClientId"/> is blank.</exception>
        public GooglePlayGamesCredentialSource(string webClientId, bool forceRefreshToken = false)
        {
            if (string.IsNullOrWhiteSpace(webClientId))
            {
                throw new ArgumentException(
                    "webClientId must be a non-empty, non-whitespace string.", nameof(webClientId));
            }

            m_webClientId = webClientId;
            m_forceRefreshToken = forceRefreshToken;
        }

        /// <inheritdoc />
        public LoginProvider Provider => LoginProvider.GooglePlayGames;

        /// <inheritdoc />
        public async Task<ProviderCredentialOutcome> AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            if (!HiveCore.TryResolve<IAuthService>(out var auth) || auth == null)
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "IAuthService is not registered. Initialize the SDK with AddAuth first."));
            }

            if (!HiveCore.TryResolve<IGooglePlayGamesPlugin>(out var games) || games == null)
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Unavailable,
                    "IGooglePlayGamesPlugin is not registered, so Play Games sign-in cannot run in this build."));
            }

            // Play Games often has a player signed in already. Asking first keeps the sign-in UI off
            // the screen when it would have nothing to ask.
            var authenticated = await games.IsAuthenticatedAsync(
                new IsAuthenticatedRequest(), cancellationToken);

            var authCheck = SdkResultClassification.Of(authenticated);
            if (authCheck.Kind == SdkResultKind.UntypedProblem)
            {
                return ProviderCredentialOutcome.Failed(authCheck.Problem);
            }

            if (authCheck.Kind == SdkResultKind.UnknownOutcome)
            {
                return ProviderCredentialOutcome.Unrecognized(
                    authCheck.UnknownCode, authCheck.RawJson);
            }

            if (authCheck.Kind != SdkResultKind.Success)
            {
                // Anything other than "already signed in" that this build cannot name must not be
                // read as "sign the player in" — only an explicit NotAuthenticated means that.
                if (!(authenticated is GooglePlayGamesServiceIsAuthenticatedResult.NotAuthenticated))
                {
                    return ProviderCredentialOutcome.Unrecognized(string.Empty, authCheck.RawJson);
                }

                var signedIn = await games.SignInAsync(new SignInRequest(), cancellationToken);
                var signInOutcome = CheckSignIn(signedIn);
                if (signInOutcome != null)
                {
                    return signInOutcome;
                }
            }

            var access = await games.RequestServerSideAccessAsync(
                new RequestServerSideAccessRequest
                {
                    WebClientId = m_webClientId,
                    ForceRefreshToken = m_forceRefreshToken,
                },
                cancellationToken);

            if (!(access is GooglePlayGamesServiceRequestServerSideAccessResult.Success accessOk))
            {
                return CheckServerSideAccess(access);
            }

            if (string.IsNullOrEmpty(accessOk.Data.ServerAuthCode))
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Internal,
                    "Play Games reported success without a server-side authorization code."));
            }

            // The code is exchanged by the server, the same way the browser route's code is. Play
            // Games has no redirect and no PKCE, so neither travels with it.
            return await ProviderCodeExchange.ExchangeAsync(
                auth, LoginProvider.GooglePlayGames, AuthProvider.GooglePlayGames,
                accessOk.Data.ServerAuthCode, null, null, cancellationToken);
        }

        /// <summary>Returns the outcome to stop on, or null when the sign-in may continue.</summary>
        private static ProviderCredentialOutcome CheckSignIn(GooglePlayGamesServiceSignInResult result)
        {
            var classification = SdkResultClassification.Of(result);
            switch (classification.Kind)
            {
                case SdkResultKind.Success:
                    return null;

                case SdkResultKind.UserCanceled:
                    return ProviderCredentialOutcome.Canceled();

                case SdkResultKind.UntypedProblem:
                    return ProviderCredentialOutcome.Failed(classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    return ProviderCredentialOutcome.Unrecognized(
                        classification.UnknownCode, classification.RawJson);

                default:
                    // Play Games declined to sign anyone in — no account on the device, or the
                    // service is unavailable here. A state of the device, not a rejected login.
                    return result is GooglePlayGamesServiceSignInResult.NotAuthenticated
                        ? ProviderCredentialOutcome.Failed(new HiveError(
                            HiveErrorCode.Unavailable,
                            "Play Games could not sign a player in on this device."))
                        : ProviderCredentialOutcome.Unrecognized(string.Empty, classification.RawJson);
            }
        }

        private static ProviderCredentialOutcome CheckServerSideAccess(
            GooglePlayGamesServiceRequestServerSideAccessResult result)
        {
            var classification = SdkResultClassification.Of(result);
            switch (classification.Kind)
            {
                case SdkResultKind.UntypedProblem:
                    return ProviderCredentialOutcome.Failed(classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    return ProviderCredentialOutcome.Unrecognized(
                        classification.UnknownCode, classification.RawJson);

                default:
                    return result is GooglePlayGamesServiceRequestServerSideAccessResult.NotAuthenticated
                        ? ProviderCredentialOutcome.Failed(new HiveError(
                            HiveErrorCode.Unavailable,
                            "Play Games has no signed-in player to issue a server-side code for."))
                        : ProviderCredentialOutcome.Unrecognized(string.Empty, classification.RawJson);
            }
        }
    }
}

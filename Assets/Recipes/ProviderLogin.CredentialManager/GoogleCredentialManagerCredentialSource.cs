// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth.Addon.CredentialManager;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Signs in with Google through Android's Credential Manager — an account picker drawn by the OS,
    /// no browser — and hands back the ID token Google issued. Needs the Credential Manager addon;
    /// without it, and anywhere but an Android player, this source fails with
    /// <see cref="HiveErrorCode.Unavailable"/>.
    /// </summary>
    /// <remarks>
    /// Uses the Google ID token and validates its nonce against this sign-in attempt.
    /// </remarks>
    public sealed class GoogleCredentialManagerCredentialSource : IProviderCredentialSource
    {
        private const string k_NonceClaim = "nonce";

        private readonly string m_webClientId;

        /// <summary>
        /// Creates the source.
        /// </summary>
        /// <param name="webClientId">
        /// The "Web application" OAuth client id from the Google Cloud Console. It becomes the
        /// token's <c>aud</c> claim, which is what the server checks the token against — the Android
        /// client id is matched by package name and signature and never passed in. Must not be null
        /// or whitespace.
        /// </param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="webClientId"/> is blank.</exception>
        public GoogleCredentialManagerCredentialSource(string webClientId)
        {
            if (string.IsNullOrWhiteSpace(webClientId))
            {
                throw new ArgumentException(
                    "webClientId must be a non-empty, non-whitespace string.", nameof(webClientId));
            }

            m_webClientId = webClientId;
        }

        /// <inheritdoc />
        public LoginProvider Provider => LoginProvider.Google;

        /// <inheritdoc />
        public async Task<ProviderCredentialOutcome> AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            if (!HiveCore.TryResolve<IAndroidCredentialManagerPlugin>(out var credentials)
                || credentials == null)
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Unavailable,
                    "IAndroidCredentialManagerPlugin is not registered, so Credential Manager sign-in "
                    + "cannot run in this build."));
            }

            // Google binds the ID token to this nonce, so it has to be unpredictable and fresh per
            // attempt — a reused one would let a token from an earlier sign-in answer this one.
            var nonce = Nonce.New();
            var result = await credentials.LoginAsync(RequestFor(nonce), cancellationToken);

            var classification = SdkResultClassification.Of(result);
            switch (classification.Kind)
            {
                case SdkResultKind.Success:
                    return CredentialFrom(result, nonce);

                case SdkResultKind.UserCanceled:
                    return ProviderCredentialOutcome.Canceled();

                case SdkResultKind.UntypedProblem:
                    return ProviderCredentialOutcome.Failed(classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    return ProviderCredentialOutcome.Unrecognized(
                        classification.UnknownCode, classification.RawJson);

                default:
                    // No Google account on the device matched the request. A state of the device,
                    // not a rejected login — the same footing as Play Games with nobody signed in.
                    return result is AndroidCredentialManagerServiceLoginResult.NoCredentials
                        ? ProviderCredentialOutcome.Failed(new HiveError(
                            HiveErrorCode.Unavailable,
                            "Credential Manager found no Google account on this device to sign in with."))
                        : ProviderCredentialOutcome.Unrecognized(string.Empty, classification.RawJson);
            }
        }

        /// <summary>
        /// Uses the "Sign in with Google" flow for a button the player pressed.
        /// The bottom-sheet option can be added as another variant of the same request.
        /// </summary>
        private LoginRequest RequestFor(string nonce) =>
            new LoginRequest
            {
                Options = new[]
                {
                    new CredentialOption
                    {
                        SignInWithGoogle = new SignInWithGoogleOption
                        {
                            WebClientId = m_webClientId,
                            Nonce = nonce,
                        },
                    },
                },
            };

        /// <summary>
        /// Turns the credential the player picked into one the server can verify, or names why it
        /// cannot be — each refusal before the token is trusted with anything.
        /// </summary>
        private static ProviderCredentialOutcome CredentialFrom(
            AndroidCredentialManagerServiceLoginResult result, string nonce)
        {
            var google = (result as AndroidCredentialManagerServiceLoginResult.Success)
                ?.Data?.Selected?.GoogleIdToken;
            if (google == null || string.IsNullOrEmpty(google.IdToken))
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Internal,
                    "Credential Manager reported success without a Google ID token."));
            }

            // A missing provider user ID is not a usable credential.
            if (string.IsNullOrEmpty(google.UniqueId))
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Internal,
                    "Credential Manager returned a Google ID token without its account id. The addon's "
                    + "Android library predates googleid 1.2.0 — rebuild it."));
            }

            // A token that does not echo this sign-in's nonce is not the answer to this request,
            // whatever else it says.
            if (!string.Equals(Jwt.ReadStringClaim(google.IdToken, k_NonceClaim), nonce, StringComparison.Ordinal))
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.PermissionDenied,
                    "The Google ID token does not carry the nonce this sign-in started with."));
            }

            return ProviderCredentialOutcome.Succeeded(
                ProviderCredential.Create(LoginProvider.Google, google.IdToken, google.UniqueId));
        }
    }
}

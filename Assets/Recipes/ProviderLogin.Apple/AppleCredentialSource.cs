// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth.Addon.Apple;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>What Sign in with Apple should ask the account holder for.</summary>
    /// <remarks>
    /// Apple returns email and name on the first sign-in only, whatever is requested afterwards, so
    /// an app that needs them must persist them the first time.
    /// </remarks>
    public sealed class AppleSignInOptions
    {
        /// <summary>Ask Apple for the account's email address.</summary>
        public bool RequestEmail { get; set; }

        /// <summary>Ask Apple for the account holder's name.</summary>
        public bool RequestFullName { get; set; }
    }

    /// <summary>
    /// Signs in with Apple and hands back the identity token the Axyl server verifies. Needs the
    /// Apple Sign-In addon; without it this source fails with
    /// <see cref="HiveErrorCode.Unavailable"/>, so an app can offer Apple only where it works.
    /// </summary>
    public sealed class AppleCredentialSource : IProviderCredentialSource
    {
        private readonly AppleSignInOptions m_options;

        /// <summary>Creates the source.</summary>
        /// <param name="options">What to request from Apple. Defaults to identifier only.</param>
        public AppleCredentialSource(AppleSignInOptions options = null)
        {
            m_options = options ?? new AppleSignInOptions();
        }

        /// <inheritdoc />
        public LoginProvider Provider => LoginProvider.Apple;

        /// <inheritdoc />
        public async Task<ProviderCredentialOutcome> AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            if (!HiveCore.TryResolve<IAppleSignInPlugin>(out var apple) || apple == null)
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Unavailable,
                    "IAppleSignInPlugin is not registered, so Apple sign-in cannot run in this build."));
            }

            // Apple binds the id_token to this nonce, so it has to be unpredictable — the same
            // reason PKCE and the WebAuth state use a CSPRNG. Nothing downstream in this flow reads
            // the raw value, so it stays here rather than being surfaced and forgotten.
            var nonce = Nonce.New();

            var result = await apple.LoginAsync(
                new AppleSignInServiceLoginRequest
                {
                    NonceHash = Pkce.Sha256Hex(nonce),
                    RequestedScopes = Scopes(),
                },
                cancellationToken);

            var classification = SdkResultClassification.Of(result);
            switch (classification.Kind)
            {
                case SdkResultKind.UserCanceled:
                    return ProviderCredentialOutcome.Canceled();

                case SdkResultKind.UntypedProblem:
                    return ProviderCredentialOutcome.Failed(classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    // Apple models every outcome other than a dismissal as an unknown code, so this
                    // is the ordinary failure path. Keep the code and body for the app to diagnose
                    // rather than collapsing them into one internal error.
                    return ProviderCredentialOutcome.Unrecognized(
                        classification.UnknownCode, classification.RawJson);

                case SdkResultKind.Success when result is AppleSignInServiceLoginResult.Success ok:
                    return ProviderCredentialOutcome.Succeeded(ProviderCredential.Create(
                        LoginProvider.Apple, ok.Data.IdentityToken, ok.Data.UserIdentifier));

                default:
                    // A declared outcome added to the addon after this build. Nothing broke, so it
                    // is reported as an outcome with no name here rather than as an internal error.
                    return ProviderCredentialOutcome.Unrecognized(
                        string.Empty, classification.RawJson);
            }
        }

        private IReadOnlyList<RequestedScope> Scopes()
        {
            var scopes = new List<RequestedScope>(2);
            if (m_options.RequestEmail)
            {
                scopes.Add(RequestedScope.Email);
            }

            if (m_options.RequestFullName)
            {
                scopes.Add(RequestedScope.FullName);
            }

            return scopes;
        }
    }
}

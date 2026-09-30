// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Logs a player in with the provider they chose — Google, Apple, Google Play Games, Steam, or X
    /// — and leaves a live session behind.
    /// </summary>
    /// <remarks>
    /// Choose a credential source supported and configured for the target platform.
    /// Unavailable sources return <see cref="HiveErrorCode.Unavailable"/>.
    /// A failed attempt leaves the existing session untouched.
    /// </remarks>
    public sealed class ProviderLoginRecipe
    {
        private readonly string m_clientId;
        private readonly string m_deviceKey;

        /// <summary>
        /// Creates the Recipe with the identity the app was provisioned with.
        /// </summary>
        /// <param name="clientId">
        /// The Axyl PKCE client id this app logs in as — not a provider's OAuth client id, which
        /// belongs to that provider's credential source. Must not be blank.
        /// </param>
        /// <param name="deviceKey">
        /// The app's device identifier. Keep it stable across launches. Must not be blank.
        /// </param>
        /// <exception cref="ArgumentException">Thrown when either argument is blank.</exception>
        public ProviderLoginRecipe(string clientId, string deviceKey)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new ArgumentException(
                    "clientId must be a non-empty, non-whitespace string.", nameof(clientId));
            }

            if (string.IsNullOrWhiteSpace(deviceKey))
            {
                throw new ArgumentException(
                    "deviceKey must be a non-empty, non-whitespace string.", nameof(deviceKey));
            }

            m_clientId = clientId;
            m_deviceKey = deviceKey;
        }

        /// <summary>
        /// Signs in with the provider behind <paramref name="credentialSource"/> and installs the
        /// session.
        /// </summary>
        /// <param name="credentialSource">
        /// The provider to sign in with. Must not be null.
        /// </param>
        /// <param name="cancellationToken">
        /// The caller's cancellation token, forwarded to the provider and to every SDK call.
        /// Cancelling it is reported as <c>Failure</c> with <see cref="HiveErrorCode.Cancelled"/>,
        /// which is not the same as the user dismissing the provider's UI.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="credentialSource"/> is null.
        /// </exception>
        public async Task<LoginWithProviderOutcome> LoginWithProviderAsync(
            IProviderCredentialSource credentialSource,
            CancellationToken cancellationToken = default)
        {
            if (credentialSource == null)
            {
                throw new ArgumentNullException(nameof(credentialSource));
            }

            var provider = credentialSource.Provider;
            if (cancellationToken.IsCancellationRequested)
            {
                return LoginWithProviderOutcome.Failed(
                    provider, LoginWithProviderStep.AcquireCredential, new HiveError(
                        HiveErrorCode.Cancelled,
                        "The caller canceled the provider login before it started."));
            }

            if (!HiveCore.TryResolve<IAuthService>(out var auth) || auth == null)
            {
                return ResolveFailed(provider,
                    "IAuthService is not registered. Initialize the SDK with AddAuth first.");
            }

            if (!HiveCore.TryResolve<ITokenService>(out var tokens) || tokens == null)
            {
                return ResolveFailed(provider,
                    "ITokenService is not registered. Initialize the SDK with AddToken first.");
            }

            if (!HiveCore.TryResolve<ISessionManager>(out var session) || session == null)
            {
                return ResolveFailed(provider,
                    "ISessionManager is not available. The SDK Core is not initialized.");
            }

            var acquired = await CredentialAcquisition.FromAsync(credentialSource, cancellationToken);
            switch (acquired.Kind)
            {
                case CredentialAcquisitionKind.Acquired:
                    break;

                case CredentialAcquisitionKind.UserCanceled:
                    return LoginWithProviderOutcome.Canceled(
                        provider, LoginWithProviderStep.AcquireCredential);

                case CredentialAcquisitionKind.ServerOutcome:
                    // A server Outcome from inside the acquisition — the provider code exchange is
                    // one — is a business result of this login, in the same vocabulary.
                    return string.IsNullOrEmpty(acquired.UnknownCode)
                        && acquired.ServerOutcome != ProviderLoginBusinessOutcome.Unrecognized
                        ? LoginWithProviderOutcome.Business(
                            provider, LoginWithProviderStep.AcquireCredential,
                            acquired.ServerOutcome, acquired.RawJson)
                        : LoginWithProviderOutcome.Unrecognized(
                            provider, LoginWithProviderStep.AcquireCredential,
                            acquired.UnknownCode, acquired.RawJson);

                default:
                    // Failed and Unusable both carry the error to report as given.
                    return LoginWithProviderOutcome.Failed(
                        provider, LoginWithProviderStep.AcquireCredential, acquired.Problem);
            }

            var credential = acquired.Credential;

            var resolution = await ProviderLoginJoin.CompleteAsync(
                auth, tokens, session, ToProvider(provider), credential.ProviderUserId,
                credential.ProviderToken, m_clientId, m_deviceKey, cancellationToken);

            return Build(provider, JoinStep(resolution), resolution);
        }

        private static Provider ToProvider(LoginProvider provider)
        {
            switch (provider)
            {
                case LoginProvider.Google:
                    return Provider.Google;
                case LoginProvider.Apple:
                    return Provider.SigninApple;
                case LoginProvider.GooglePlayGames:
                    return Provider.GooglePlayGames;
                case LoginProvider.Steam:
                    return Provider.Steam;
                default:
                    return Provider.X;
            }
        }

        private static LoginWithProviderOutcome ResolveFailed(LoginProvider provider, string message) =>
            LoginWithProviderOutcome.Failed(
                provider, LoginWithProviderStep.Resolve,
                new HiveError(HiveErrorCode.FailedPrecondition, message));

        /// <summary>Names a resolved step in this Recipe's vocabulary.</summary>
        private static LoginWithProviderOutcome Build(
            LoginProvider provider, LoginWithProviderStep step, ProviderLoginResolution resolution)
        {
            switch (resolution.Kind)
            {
                case ProviderLoginResolutionKind.Success:
                    return LoginWithProviderOutcome.Succeeded(
                        provider, resolution.PlayerId, resolution.IsBlocked);

                case ProviderLoginResolutionKind.Canceled:
                    return LoginWithProviderOutcome.Canceled(provider, step);

                case ProviderLoginResolutionKind.Failed:
                    return LoginWithProviderOutcome.Failed(provider, step, resolution.Error);

                case ProviderLoginResolutionKind.Business:
                    return LoginWithProviderOutcome.Business(
                        provider, step, resolution.BusinessOutcome, resolution.RawJson);

                default:
                    return LoginWithProviderOutcome.Unrecognized(
                        provider, step, resolution.UnknownOutcomeCode, resolution.RawJson);
            }
        }

        private static LoginWithProviderStep JoinStep(ProviderLoginResolution resolution) =>
            resolution.JoinStep == ProviderLoginJoinStep.LoginProvider
                ? LoginWithProviderStep.LoginProvider
                : LoginWithProviderStep.SessionSetup;
    }
}

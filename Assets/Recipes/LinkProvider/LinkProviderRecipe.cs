// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Attaches a provider account to the player who is already signed in, so they can sign in with
    /// it next time.
    /// </summary>
    /// <remarks>
    /// Requires initialized Core with <c>AddAuth</c> and an active session.
    /// Use <see cref="LinkProviderOutcome.IsConflict"/> and <see cref="LinkProviderOutcome.BusinessOutcome"/>
    /// to handle account conflicts. Do not automatically switch accounts or retry a conflict.
    /// After success, remove stored guest credentials only when their player ID matches
    /// <see cref="LinkProviderOutcome.PlayerId"/>. Use the linked provider for future sign-in.
    /// </remarks>
    public sealed class LinkProviderRecipe
    {
        /// <summary>
        /// Links the provider behind <paramref name="credentialSource"/> to the signed-in player.
        /// </summary>
        /// <param name="credentialSource">The provider to attach. Must not be null.</param>
        /// <param name="cancellationToken">
        /// The caller's cancellation token, forwarded to the provider and to the SDK call. Cancelling
        /// it is reported as <c>Failure</c> with <see cref="HiveErrorCode.Cancelled"/>, which is not
        /// the same as the user dismissing the provider's UI.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="credentialSource"/> is null.
        /// </exception>
        public async Task<LinkProviderOutcome> LinkProviderAsync(
            IProviderCredentialSource credentialSource,
            CancellationToken cancellationToken = default)
        {
            if (credentialSource == null)
            {
                throw new ArgumentNullException(nameof(credentialSource));
            }

            var provider = credentialSource.Provider;

            // Before the preconditions: a caller who already canceled is told why they stopped
            // rather than what else was missing.
            if (cancellationToken.IsCancellationRequested)
            {
                return LinkProviderOutcome.Failed(
                    provider, LinkProviderStep.Resolve, new HiveError(
                        HiveErrorCode.Cancelled, "The caller canceled the link before it started."));
            }

            if (!HiveCore.TryResolve<IAuthService>(out var auth) || auth == null)
            {
                return LinkProviderOutcome.Failed(
                    provider, LinkProviderStep.Resolve, new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "IAuthService is not registered. Initialize the SDK with AddAuth first."));
            }

            if (!HiveCore.TryResolve<ISessionManager>(out var session) || session == null)
            {
                return LinkProviderOutcome.Failed(
                    provider, LinkProviderStep.Resolve, new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "ISessionManager is not available. The SDK Core is not initialized."));
            }

            // Reported before the provider's UI opens. Sending the player through a sign-in only to
            // find there was nobody to link it to wastes the one step that costs them attention.
            if (!session.IsLoggedIn)
            {
                return LinkProviderOutcome.Failed(
                    provider, LinkProviderStep.Resolve, new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "No session is active, so there is no player to link a provider to."));
            }

            // Capture the session token before provider authentication. Abort if it changes before
            // linking, whether due to account switching or a refresh for the same player.
            var sessionAtStart = session.AccessToken;

            var acquired = await CredentialAcquisition.FromAsync(credentialSource, cancellationToken);

            // A cancel does not reach into a provider UI already open, so an answer can land after
            // the caller stopped waiting. It must not go on to change the account.
            if (cancellationToken.IsCancellationRequested)
            {
                return LinkProviderOutcome.Failed(
                    provider, LinkProviderStep.AcquireCredential, new HiveError(
                        HiveErrorCode.Cancelled, "The link was canceled."));
            }

            switch (acquired.Kind)
            {
                case CredentialAcquisitionKind.Acquired:
                    break;

                case CredentialAcquisitionKind.UserCanceled:
                    return LinkProviderOutcome.Canceled(
                        provider, LinkProviderStep.AcquireCredential);

                case CredentialAcquisitionKind.ServerOutcome:
                    // A server Outcome from inside the acquisition arrives in the login Recipe's
                    // vocabulary, since that is what the sources report. Translate rather than
                    // forward: this is a link result, and the two vocabularies do not overlap
                    // everywhere.
                    return FromAcquisitionOutcome(provider, acquired);

                default:
                    return LinkProviderOutcome.Failed(
                        provider, LinkProviderStep.AcquireCredential, acquired.Problem);
            }

            if (!string.Equals(session.AccessToken, sessionAtStart, StringComparison.Ordinal))
            {
                return LinkProviderOutcome.Failed(
                    provider, LinkProviderStep.Link, new HiveError(
                        HiveErrorCode.Aborted,
                        "The session access token changed during provider authentication. "
                        + "The link was not sent; confirm the current session before trying again."));
            }

            var credential = acquired.Credential;

            var result = await auth.LinkProviderAsync(
                new ProviderLinkRequest
                {
                    ProviderId = ToProvider(provider),
                    ProviderUserId = credential.ProviderUserId,
                    ProviderToken = credential.ProviderToken,
                },
                new ApiCallContext { Token = cancellationToken });

            if (cancellationToken.IsCancellationRequested)
            {
                // The server may well have linked it. Do not report success after the caller stopped
                // waiting; the app re-reads the player's providers to find out.
                return LinkProviderOutcome.Failed(
                    provider, LinkProviderStep.Link, new HiveError(
                        HiveErrorCode.Cancelled, "The link was canceled."));
            }

            var classification = SdkResultClassification.Of(result);
            switch (classification.Kind)
            {
                case SdkResultKind.Success:
                    var linked = (result as AuthLinkProviderResult.Success)?.Data;
                    return LinkProviderOutcome.Succeeded(
                        provider, linked?.PlayerId ?? 0, linked?.ProviderUserId);

                case SdkResultKind.UserCanceled:
                case SdkResultKind.UntypedProblem:
                    return LinkProviderOutcome.Failed(
                        provider, LinkProviderStep.Link, classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    return LinkProviderOutcome.Unrecognized(
                        provider, LinkProviderStep.Link, classification.UnknownCode,
                        classification.RawJson);

                default:
                    var businessOutcome = Map(result);
                    return businessOutcome == LinkProviderBusinessOutcome.Unrecognized
                        ? LinkProviderOutcome.Unrecognized(
                            provider, LinkProviderStep.Link, string.Empty, classification.RawJson)
                        : LinkProviderOutcome.Business(
                            provider, LinkProviderStep.Link, businessOutcome,
                            classification.RawJson);
            }
        }

        /// <summary>
        /// Carries a server Outcome raised inside the credential acquisition across into this
        /// Recipe's vocabulary. Only the values that mean the same thing on both sides are mapped;
        /// anything else stays unrecognized rather than being bent into a neighbouring meaning.
        /// </summary>
        private static LinkProviderOutcome FromAcquisitionOutcome(
            LoginProvider provider, CredentialAcquisition acquired)
        {
            if (!string.IsNullOrEmpty(acquired.UnknownCode))
            {
                return LinkProviderOutcome.Unrecognized(
                    provider, LinkProviderStep.AcquireCredential, acquired.UnknownCode,
                    acquired.RawJson);
            }

            LinkProviderBusinessOutcome mapped;
            switch (acquired.ServerOutcome)
            {
                case ProviderLoginBusinessOutcome.ServiceTerminated:
                    mapped = LinkProviderBusinessOutcome.ServiceTerminated;
                    break;
                case ProviderLoginBusinessOutcome.AppNotFound:
                    mapped = LinkProviderBusinessOutcome.AppNotFound;
                    break;
                case ProviderLoginBusinessOutcome.ProviderNotSupported:
                    mapped = LinkProviderBusinessOutcome.ProviderNotSupported;
                    break;
                case ProviderLoginBusinessOutcome.ProviderConfigNotFound:
                    mapped = LinkProviderBusinessOutcome.ProviderConfigNotFound;
                    break;
                case ProviderLoginBusinessOutcome.ProviderRequestFailed:
                    mapped = LinkProviderBusinessOutcome.ProviderRequestFailed;
                    break;
                case ProviderLoginBusinessOutcome.ProviderTokenError:
                    mapped = LinkProviderBusinessOutcome.ProviderTokenError;
                    break;
                case ProviderLoginBusinessOutcome.ProviderClientInfoNotExists:
                    mapped = LinkProviderBusinessOutcome.ProviderClientInfoNotExists;
                    break;
                default:
                    mapped = LinkProviderBusinessOutcome.Unrecognized;
                    break;
            }

            return mapped == LinkProviderBusinessOutcome.Unrecognized
                ? LinkProviderOutcome.Unrecognized(
                    provider, LinkProviderStep.AcquireCredential, string.Empty, acquired.RawJson)
                : LinkProviderOutcome.Business(
                    provider, LinkProviderStep.AcquireCredential, mapped, acquired.RawJson);
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

        private static LinkProviderBusinessOutcome Map(AuthLinkProviderResult result)
        {
            switch (result)
            {
                // The conflicts first: these are what the Recipe exists to surface.
                case AuthLinkProviderResult.ProviderOwnedByOther _:
                    return LinkProviderBusinessOutcome.ProviderOwnedByOther;
                case AuthLinkProviderResult.ProviderTypeAlreadyExists _:
                    return LinkProviderBusinessOutcome.ProviderTypeAlreadyExists;
                case AuthLinkProviderResult.ProviderAlreadyConnected _:
                    return LinkProviderBusinessOutcome.ProviderAlreadyConnected;
                case AuthLinkProviderResult.ProviderTokenError _:
                    return LinkProviderBusinessOutcome.ProviderTokenError;
                case AuthLinkProviderResult.ProviderRequestFailed _:
                    return LinkProviderBusinessOutcome.ProviderRequestFailed;
                case AuthLinkProviderResult.ProviderConfigNotFound _:
                    return LinkProviderBusinessOutcome.ProviderConfigNotFound;
                case AuthLinkProviderResult.ProviderClientInfoNotExists _:
                    return LinkProviderBusinessOutcome.ProviderClientInfoNotExists;
                case AuthLinkProviderResult.TerminateService _:
                    return LinkProviderBusinessOutcome.ServiceTerminated;
                case AuthLinkProviderResult.AppNotFound _:
                    return LinkProviderBusinessOutcome.AppNotFound;

                case AuthLinkProviderResult.UsernameVerifyFailed _:
                    return LinkProviderBusinessOutcome.UsernameVerifyFailed;
                case AuthLinkProviderResult.UsernameAlreadyExists _:
                    return LinkProviderBusinessOutcome.UsernameAlreadyExists;
                case AuthLinkProviderResult.InvalidUsernameFormat _:
                    return LinkProviderBusinessOutcome.InvalidUsernameFormat;
                case AuthLinkProviderResult.InvalidPasswordFormat _:
                    return LinkProviderBusinessOutcome.InvalidPasswordFormat;

                case AuthLinkProviderResult.AppIdMismatch _:
                    return LinkProviderBusinessOutcome.AppIdMismatch;
                case AuthLinkProviderResult.InvalidGatewayContext _:
                    return LinkProviderBusinessOutcome.InvalidGatewayContext;
                case AuthLinkProviderResult.IpBlocked _:
                    return LinkProviderBusinessOutcome.IpBlocked;
                case AuthLinkProviderResult.PlayerNotFound _:
                    return LinkProviderBusinessOutcome.PlayerNotFound;

                default:
                    return LinkProviderBusinessOutcome.Unrecognized;
            }
        }
    }
}

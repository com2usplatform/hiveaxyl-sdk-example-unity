// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth.Addon.Steam;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Signs in with Steam and hands back the Web API auth ticket the Axyl server verifies. Needs the
    /// Steam addon and a Steam client the player is signed in to; either one missing fails with
    /// <see cref="HiveErrorCode.Unavailable"/>, which says the environment cannot run Steam here
    /// rather than that the sign-in was rejected.
    /// </summary>
    public sealed class SteamCredentialSource : IProviderCredentialSource
    {
        private readonly string m_identity;
        private readonly string m_steamId;

        /// <summary>
        /// Creates the source.
        /// </summary>
        /// <param name="identity">
        /// The Web API identity string the ticket is issued for. The backend validates the ticket
        /// against the identical string, so this is a deployment agreement rather than something the
        /// Recipe can derive. Must not be null or whitespace.
        /// </param>
        /// <param name="steamId">
        /// The SteamID to send as the provider user id. Reading it needs Steamworks, which the app
        /// owns. Empty when the app has none, which the server may or may not accept.
        /// </param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="identity"/> is blank.</exception>
        public SteamCredentialSource(string identity, string steamId = null)
        {
            if (string.IsNullOrWhiteSpace(identity))
            {
                throw new ArgumentException(
                    "identity must be a non-empty, non-whitespace string.", nameof(identity));
            }

            m_identity = identity;
            m_steamId = steamId ?? string.Empty;
        }

        /// <inheritdoc />
        public LoginProvider Provider => LoginProvider.Steam;

        /// <inheritdoc />
        public async Task<ProviderCredentialOutcome> AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            if (!HiveCore.TryResolve<ISteamPlugin>(out var steam) || steam == null)
            {
                return ProviderCredentialOutcome.Failed(new HiveError(
                    HiveErrorCode.Unavailable,
                    "ISteamPlugin is not registered, so Steam sign-in cannot run in this build."));
            }

            var result = await steam.GetAuthTicketForWebApiAsync(
                new GetAuthTicketForWebApiRequest { Identity = m_identity }, cancellationToken);

            var classification = SdkResultClassification.Of(result);
            switch (classification.Kind)
            {
                case SdkResultKind.UntypedProblem:
                    return ProviderCredentialOutcome.Failed(classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    // A code newer than this build: keep it and the body so the app can diagnose,
                    // rather than reporting an internal error that says nothing.
                    return ProviderCredentialOutcome.Unrecognized(
                        classification.UnknownCode, classification.RawJson);

                case SdkResultKind.Success
                    when result is SteamServiceGetAuthTicketForWebApiResult.Success ok:
                    // The SteamID goes up as the provider user id. Reading it needs Steamworks,
                    // which the app owns, so it arrives as an argument rather than being looked up
                    // here.
                    return ProviderCredentialOutcome.Succeeded(ProviderCredential.Create(
                        LoginProvider.Steam, ok.Data.TicketHex, m_steamId));

                case SdkResultKind.TypedOutcome
                    when result is SteamServiceGetAuthTicketForWebApiResult.NotAuthenticated:
                    // Nobody is signed in to the Steam client. That is the state of the machine, not
                    // a rejected login, so the app can hide or disable Steam rather than show an error.
                    return ProviderCredentialOutcome.Failed(new HiveError(
                        HiveErrorCode.Unavailable,
                        "No player is signed in to the Steam client."));

                default:
                    // A declared outcome added to the addon after this build. Nothing broke, so it
                    // is reported as an outcome with no name here rather than as an internal error.
                    return ProviderCredentialOutcome.Unrecognized(
                        string.Empty, classification.RawJson);
            }
        }
    }
}

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The providers a player can log in with.</summary>
    public enum LoginProvider
    {
        /// <summary>Google.</summary>
        Google,

        /// <summary>Sign in with Apple.</summary>
        Apple,

        /// <summary>Google Play Games.</summary>
        GooglePlayGames,

        /// <summary>Steam.</summary>
        Steam,

        /// <summary>X (formerly Twitter).</summary>
        X,
    }

    /// <summary>What a provider proved about the player, in the form the Axyl server verifies.</summary>
    public sealed class ProviderCredential
    {
        private ProviderCredential(LoginProvider provider, string providerUserId, string providerToken)
        {
            Provider = provider;
            ProviderUserId = providerUserId;
            ProviderToken = providerToken;
        }

        /// <summary>The provider this credential came from.</summary>
        public LoginProvider Provider { get; }

        /// <summary>
        /// The provider's user id, or empty when the server derives it from the token itself — Steam
        /// reads the SteamID out of the ticket, so a client-supplied id would be guesswork.
        /// </summary>
        public string ProviderUserId { get; }

        /// <summary>The token or ticket the server verifies with the provider.</summary>
        public string ProviderToken { get; }

        /// <summary>
        /// Creates a credential.
        /// </summary>
        /// <param name="provider">The provider it came from.</param>
        /// <param name="providerToken">The token or ticket. Must not be null or whitespace.</param>
        /// <param name="providerUserId">
        /// The provider's user id, or null when the server derives it from the token.
        /// </param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="providerToken"/> is blank.</exception>
        public static ProviderCredential Create(
            LoginProvider provider, string providerToken, string providerUserId = null)
        {
            if (string.IsNullOrWhiteSpace(providerToken))
            {
                throw new ArgumentException(
                    "providerToken must be a non-empty, non-whitespace string.", nameof(providerToken));
            }

            return new ProviderCredential(provider, providerUserId ?? string.Empty, providerToken);
        }
    }

    /// <summary>How a credential acquisition ended.</summary>
    public enum ProviderCredentialStatus
    {
        /// <summary>The provider issued a credential.</summary>
        Success,

        /// <summary>
        /// A server call inside the acquisition answered with a non-success Outcome — the provider
        /// code exchange is one. Carried as a value, never reduced to a message.
        /// </summary>
        BusinessOutcome,

        /// <summary>The user dismissed the provider's UI, or declined at its consent screen.</summary>
        UserCanceled,

        /// <summary>
        /// A technical failure while acquiring the credential. A provider that cannot be used here at
        /// all is one of these too, carrying <see cref="HiveErrorCode.Unavailable"/> — the environment
        /// is read from the error code, never from a status of its own.
        /// </summary>
        Failure,
    }

    /// <summary>The result of asking a provider for a credential.</summary>
    public sealed class ProviderCredentialOutcome
    {
        private ProviderCredentialOutcome(
            ProviderCredentialStatus status,
            ProviderCredential credential,
            ProviderLoginBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            Credential = credential;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the acquisition ended.</summary>
        public ProviderCredentialStatus Status { get; }

        /// <summary>The credential. Non-null only on <see cref="ProviderCredentialStatus.Success"/>.</summary>
        public ProviderCredential Credential { get; }

        /// <summary>
        /// The translated business result. Meaningful only on
        /// <see cref="ProviderCredentialStatus.BusinessOutcome"/>. Shares the vocabulary the Recipe
        /// answers in, so a server Outcome means the same thing wherever in the login it arose.
        /// </summary>
        public ProviderLoginBusinessOutcome BusinessOutcome { get; }

        /// <summary>The server's Outcome code when this SDK build did not recognize it, or empty.</summary>
        public string UnknownOutcomeCode { get; }

        /// <summary>The unparsed response body, for diagnosis only.</summary>
        public string RawJson { get; }

        /// <summary>
        /// The preserved error, and the only place the kind of failure is recorded. Non-null on
        /// <see cref="ProviderCredentialStatus.Failure"/>.
        /// </summary>
        public HiveError Error { get; }

        /// <summary>The provider issued a credential.</summary>
        public static ProviderCredentialOutcome Succeeded(ProviderCredential credential) =>
            new ProviderCredentialOutcome(
                ProviderCredentialStatus.Success,
                credential ?? throw new ArgumentNullException(nameof(credential)),
                ProviderLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        /// <summary>A server call inside the acquisition answered with a translated Outcome.</summary>
        public static ProviderCredentialOutcome Business(
            ProviderLoginBusinessOutcome businessOutcome, string rawJson = null) =>
            new ProviderCredentialOutcome(
                ProviderCredentialStatus.BusinessOutcome, null, businessOutcome, string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>
        /// A server call answered with an Outcome no value is claimed for — unknown to this SDK
        /// build (<paramref name="unknownOutcomeCode"/> non-empty) or unmapped here.
        /// </summary>
        public static ProviderCredentialOutcome Unrecognized(
            string unknownOutcomeCode, string rawJson = null) =>
            new ProviderCredentialOutcome(
                ProviderCredentialStatus.BusinessOutcome, null,
                ProviderLoginBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>The user dismissed the provider's UI or declined consent.</summary>
        public static ProviderCredentialOutcome Canceled() =>
            new ProviderCredentialOutcome(
                ProviderCredentialStatus.UserCanceled, null,
                ProviderLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        /// <summary>
        /// A technical failure. A provider that cannot be used here at all is one of these, carrying
        /// <see cref="HiveErrorCode.Unavailable"/> — the environment is told apart by the error code,
        /// never by a status or a factory of its own.
        /// </summary>
        public static ProviderCredentialOutcome Failed(HiveError error) =>
            new ProviderCredentialOutcome(
                ProviderCredentialStatus.Failure, null,
                ProviderLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty,
                error ?? throw new ArgumentNullException(nameof(error)));
    }

    /// <summary>
    /// Obtains a provider credential for <see cref="ProviderLoginRecipe"/>. One implementation per
    /// authentication route, each shipped with its addon dependencies, so a game installs
    /// only the providers it signs in with. The app picks which one to use and hands it in.
    /// </summary>
    /// <remarks>
    /// Implementations obtain credentials through native SDK calls or browser authentication.
    /// Choose a supported route for the platform. Only providers in <see cref="LoginProvider"/> are supported.
    /// </remarks>
    public interface IProviderCredentialSource
    {
        /// <summary>The provider this source signs in with.</summary>
        LoginProvider Provider { get; }

        /// <summary>
        /// Asks the provider for a credential.
        /// </summary>
        /// <param name="cancellationToken">
        /// The caller's token. Cancelling it is a caller cancellation, distinct from the user
        /// dismissing the provider's UI.
        /// </param>
        Task<ProviderCredentialOutcome> AcquireAsync(CancellationToken cancellationToken = default);
    }
}

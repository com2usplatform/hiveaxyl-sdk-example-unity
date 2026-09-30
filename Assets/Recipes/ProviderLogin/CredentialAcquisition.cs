// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>What came back from asking a credential source.</summary>
    internal enum CredentialAcquisitionKind
    {
        /// <summary>A usable credential is in hand.</summary>
        Acquired,

        /// <summary>The user dismissed an OS or provider UI.</summary>
        UserCanceled,

        /// <summary>A server answered a step inside the acquisition with a non-success Outcome.</summary>
        ServerOutcome,

        /// <summary>The acquisition failed technically; the source's error is preserved.</summary>
        Failed,

        /// <summary>
        /// The source reported success but handed back nothing a login or link could be built from.
        /// A bug in the source, reported rather than thrown out of a Recipe step.
        /// </summary>
        Unusable,
    }

    /// <summary>
    /// One credential-source answer, sorted into the states every Recipe that uses a source has to
    /// handle, carrying what each state must preserve.
    /// </summary>
    /// <remarks>
    /// Preserves the credential source's business outcome, unknown code, and diagnostic response.
    /// </remarks>
    internal readonly struct CredentialAcquisition
    {
        private CredentialAcquisition(
            CredentialAcquisitionKind kind,
            ProviderCredential credential,
            ProviderLoginBusinessOutcome serverOutcome,
            string unknownCode,
            string rawJson,
            HiveError problem)
        {
            Kind = kind;
            Credential = credential;
            ServerOutcome = serverOutcome;
            UnknownCode = unknownCode;
            RawJson = rawJson;
            Problem = problem;
        }

        /// <summary>Which state this answer belongs to.</summary>
        public CredentialAcquisitionKind Kind { get; }

        /// <summary>
        /// The credential to send. Non-null only for <see cref="CredentialAcquisitionKind.Acquired"/>,
        /// where its token is already known to be non-empty.
        /// </summary>
        public ProviderCredential Credential { get; }

        /// <summary>
        /// The server's business outcome as the source reported it. Meaningful only for
        /// <see cref="CredentialAcquisitionKind.ServerOutcome"/>, and only when
        /// <see cref="UnknownCode"/> is empty.
        /// </summary>
        public ProviderLoginBusinessOutcome ServerOutcome { get; }

        /// <summary>
        /// The server's outcome code when this SDK build did not recognize it. Non-empty only for
        /// <see cref="CredentialAcquisitionKind.ServerOutcome"/>.
        /// </summary>
        public string UnknownCode { get; }

        /// <summary>The unparsed response body, for diagnosis only. Never branch on it.</summary>
        public string RawJson { get; }

        /// <summary>
        /// The error to report. Non-null for <see cref="CredentialAcquisitionKind.Failed"/> and
        /// <see cref="CredentialAcquisitionKind.Unusable"/>.
        /// </summary>
        public HiveError Problem { get; }

        /// <summary>
        /// Asks <paramref name="source"/> for a credential and sorts the answer.
        /// </summary>
        /// <param name="source">The credential source to ask. Must not be null.</param>
        /// <param name="cancellationToken">The caller's token, forwarded to the source.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="source"/> is null.
        /// </exception>
        internal static async Task<CredentialAcquisition> FromAsync(
            IProviderCredentialSource source, CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var outcome = await source.AcquireAsync(cancellationToken);
            if (outcome == null)
            {
                return Unusable(new HiveError(
                    HiveErrorCode.Internal,
                    $"The credential source for {source.Provider} returned no outcome."));
            }

            switch (outcome.Status)
            {
                case ProviderCredentialStatus.UserCanceled:
                    return new CredentialAcquisition(
                        CredentialAcquisitionKind.UserCanceled, null,
                        ProviderLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

                case ProviderCredentialStatus.BusinessOutcome:
                    return new CredentialAcquisition(
                        CredentialAcquisitionKind.ServerOutcome, null, outcome.BusinessOutcome,
                        outcome.UnknownOutcomeCode ?? string.Empty, outcome.RawJson ?? string.Empty,
                        null);

                case ProviderCredentialStatus.Success:
                    break;

                default:
                    // The source's error passes through untouched. Its code is what tells the app
                    // whether this provider is usable here at all — Unavailable if it is not.
                    return new CredentialAcquisition(
                        CredentialAcquisitionKind.Failed, null,
                        ProviderLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty,
                        outcome.Error);
            }

            var credential = outcome.Credential;
            if (credential == null || string.IsNullOrEmpty(credential.ProviderToken))
            {
                return Unusable(new HiveError(
                    HiveErrorCode.Internal,
                    $"The credential source for {source.Provider} reported success without a token."));
            }

            return new CredentialAcquisition(
                CredentialAcquisitionKind.Acquired, credential,
                ProviderLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);
        }

        private static CredentialAcquisition Unusable(HiveError problem) =>
            new CredentialAcquisition(
                CredentialAcquisitionKind.Unusable, null,
                ProviderLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty, problem);
    }
}

// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>Which outcome shape a provider-login step resolved to.</summary>
    internal enum ProviderLoginResolutionKind
    {
        /// <summary>The login completed and the session is live.</summary>
        Success,

        /// <summary>A translated business result.</summary>
        Business,

        /// <summary>An Outcome no value is claimed for — unknown to the SDK, or unmapped here.</summary>
        Unrecognized,

        /// <summary>The user dismissed an OS or provider UI.</summary>
        Canceled,

        /// <summary>A technical failure.</summary>
        Failed,
    }

    /// <summary>Which half of the shared join a resolution came from.</summary>
    internal enum ProviderLoginJoinStep
    {
        /// <summary>The server-side provider login.</summary>
        LoginProvider,

        /// <summary>The token exchange and session install that follows it.</summary>
        SessionSetup,
    }

    /// <summary>
    /// The result of a provider-login step.
    /// </summary>
    internal readonly struct ProviderLoginResolution
    {
        private ProviderLoginResolution(
            ProviderLoginResolutionKind kind,
            ProviderLoginJoinStep joinStep,
            long playerId,
            bool isBlocked,
            ProviderLoginBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Kind = kind;
            JoinStep = joinStep;
            PlayerId = playerId;
            IsBlocked = isBlocked;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>The shape this step resolved to.</summary>
        public ProviderLoginResolutionKind Kind { get; }

        /// <summary>
        /// Which half of the shared join produced this. Steps a Recipe runs before the join leave it
        /// at its default, since the Recipe already knows where it is.
        /// </summary>
        public ProviderLoginJoinStep JoinStep { get; }

        /// <summary>
        /// The player that was logged in. Set only for
        /// <see cref="ProviderLoginResolutionKind.Success"/>.
        /// </summary>
        public long PlayerId { get; }

        /// <summary>Whether the server reports this player as blocked. Set only on success.</summary>
        public bool IsBlocked { get; }

        /// <summary>
        /// The translated business result. Set only for
        /// <see cref="ProviderLoginResolutionKind.Business"/>.
        /// </summary>
        public ProviderLoginBusinessOutcome BusinessOutcome { get; }

        /// <summary>The server's unrecognized outcome code, or empty.</summary>
        public string UnknownOutcomeCode { get; }

        /// <summary>The unparsed response body, for diagnosis only.</summary>
        public string RawJson { get; }

        /// <summary>
        /// The preserved SDK error. Non-null only for
        /// <see cref="ProviderLoginResolutionKind.Failed"/>.
        /// </summary>
        public HiveError Error { get; }

        /// <summary>
        /// Reduces a classified SDK result. Only the typed-Outcome branch consults
        /// <paramref name="businessOutcome"/>; every other branch already describes itself.
        /// </summary>
        internal static ProviderLoginResolution FromClassification(
            SdkResultClassification classification, ProviderLoginBusinessOutcome businessOutcome)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UserCanceled:
                    return new ProviderLoginResolution(
                        ProviderLoginResolutionKind.Canceled, default, 0, false,
                        ProviderLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

                case SdkResultKind.UntypedProblem:
                    return Failed(classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    return Unrecognized(classification.UnknownCode, classification.RawJson);

                default:
                    // A typed Outcome this Recipe has no value for stays unrecognized rather than
                    // being folded into a neighbouring meaning.
                    return businessOutcome == ProviderLoginBusinessOutcome.Unrecognized
                        ? Unrecognized(string.Empty, classification.RawJson)
                        : new ProviderLoginResolution(
                            ProviderLoginResolutionKind.Business, default, 0, false,
                            businessOutcome, string.Empty, classification.RawJson ?? string.Empty,
                            null);
            }
        }

        /// <summary>The login completed and the session is live.</summary>
        internal static ProviderLoginResolution Established(
            long playerId, bool isBlocked) =>
            new ProviderLoginResolution(
                ProviderLoginResolutionKind.Success, ProviderLoginJoinStep.SessionSetup, playerId,
                isBlocked, ProviderLoginBusinessOutcome.Unrecognized, string.Empty,
                string.Empty, null);

        /// <summary>A technical failure, preserving the SDK error as given.</summary>
        internal static ProviderLoginResolution Failed(HiveError error) =>
            new ProviderLoginResolution(
                ProviderLoginResolutionKind.Failed, default, 0, false,
                ProviderLoginBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);

        /// <summary>An Outcome no value is claimed for.</summary>
        internal static ProviderLoginResolution Unrecognized(string unknownOutcomeCode, string rawJson) =>
            new ProviderLoginResolution(
                ProviderLoginResolutionKind.Unrecognized, default, 0, false,
                ProviderLoginBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>Stamps which half of the shared join this came from.</summary>
        internal ProviderLoginResolution AtJoinStep(ProviderLoginJoinStep step) =>
            new ProviderLoginResolution(
                Kind, step, PlayerId, IsBlocked, BusinessOutcome, UnknownOutcomeCode,
                RawJson, Error);
    }
}

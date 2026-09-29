// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using Hive.Axyl.Contracts.Result;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>Which Recipe result state an SDK result belongs to.</summary>
    internal enum SdkResultKind
    {
        /// <summary>The call completed the step it was asked to perform.</summary>
        Success,

        /// <summary>A domain Outcome this build of the SDK models as its own result variant.</summary>
        TypedOutcome,

        /// <summary>An Outcome code this build of the SDK does not recognize.</summary>
        UnknownOutcome,

        /// <summary>The user dismissed an OS or provider UI.</summary>
        UserCanceled,

        /// <summary>A technical failure: network, timeout, parsing, cancellation, or precondition.</summary>
        UntypedProblem,
    }

    /// <summary>
    /// An SDK result classified for Recipe handling. Recipes map typed business outcomes separately.
    /// </summary>
    internal readonly struct SdkResultClassification
    {
        private SdkResultClassification(SdkResultKind kind, HiveError problem, string unknownCode, string rawJson)
        {
            Kind = kind;
            Problem = problem;
            UnknownCode = unknownCode;
            RawJson = rawJson;
        }

        /// <summary>The Recipe result state this SDK result belongs to.</summary>
        public SdkResultKind Kind { get; }

        /// <summary>
        /// The SDK error to preserve verbatim. Non-null only for
        /// <see cref="SdkResultKind.UntypedProblem"/>.
        /// </summary>
        public HiveError Problem { get; }

        /// <summary>
        /// The server's unrecognized outcome code. Non-empty only for
        /// <see cref="SdkResultKind.UnknownOutcome"/>; never mapped onto a known Business Outcome.
        /// </summary>
        public string UnknownCode { get; }

        /// <summary>
        /// The unparsed response body, kept for forward-compatibility diagnosis only. Empty when the
        /// call produced no body. Never branch on this and never show it to a player.
        /// </summary>
        public string RawJson { get; }

        /// <summary>
        /// Sorts an SDK result into a Recipe result state.
        /// </summary>
        /// <param name="result">The result an SDK call returned. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="result"/> is null.</exception>
        public static SdkResultClassification Of(IAxylResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var raw = result.RawResponse ?? string.Empty;

            if (result.IsSuccess)
            {
                return new SdkResultClassification(SdkResultKind.Success, null, string.Empty, raw);
            }

            // A user who dismissed an OS or provider UI is reported as canceled even when the SDK
            // models that dismissal as one of its typed Outcome variants.
            if (result is IUserCanceledOutcome)
            {
                return new SdkResultClassification(SdkResultKind.UserCanceled, null, string.Empty, raw);
            }

            if (result is IUnknownOutcome unknown)
            {
                return new SdkResultClassification(
                    SdkResultKind.UnknownOutcome, null, unknown.Code, unknown.RawJson);
            }

            if (result is IUntypedProblem untyped)
            {
                return new SdkResultClassification(
                    SdkResultKind.UntypedProblem, untyped.Problem, string.Empty, raw);
            }

            // The base class flags an untyped problem independently of the marker interface. Trust
            // the flag so a variant that ever ships without the marker is still not read as a
            // domain Outcome.
            if (result.IsUntypedProblem && result.UntypedProblem != null)
            {
                return new SdkResultClassification(
                    SdkResultKind.UntypedProblem, result.UntypedProblem, string.Empty, raw);
            }

            return new SdkResultClassification(SdkResultKind.TypedOutcome, null, string.Empty, raw);
        }
    }
}

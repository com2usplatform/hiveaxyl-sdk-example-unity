// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Collections.Generic;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of one attempt to find undelivered purchases.</summary>
    public enum FindUndeliveredStatus
    {
        /// <summary>The server answered. <c>Undelivered</c> holds what it found — possibly nothing.</summary>
        Success,

        /// <summary>The server answered with a non-success Outcome. See <c>BusinessOutcome</c>.</summary>
        BusinessOutcome,

        /// <summary>A technical failure. See <c>Error</c> for the preserved <see cref="HiveError"/>.</summary>
        Failure,
    }

    /// <summary>Which step of the find did not complete.</summary>
    public enum FindUndeliveredStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Checking the query the caller supplied, before any call is made.</summary>
        Validate,

        /// <summary>Resolving the SDK services the find needs.</summary>
        Resolve,

        /// <summary>
        /// Querying the server for open purchases.
        /// </summary>
        Restore,

        /// <summary>Asking the market's own store for its open purchases.</summary>
        Store,
    }

    /// <summary>
    /// The result of asking the server for undelivered purchases. Read <see cref="Status"/> first; the
    /// other members are meaningful per the state documented on each.
    /// </summary>
    /// <remarks>
    /// An empty successful result means no open purchases were found within the query scope.
    /// </remarks>
    public sealed class FindUndeliveredOutcome
    {
        private static readonly IReadOnlyList<PendingPurchase> s_none = new PendingPurchase[0];

        private FindUndeliveredOutcome(
            FindUndeliveredStatus status,
            FindUndeliveredStep failedStep,
            IReadOnlyList<PendingPurchase> undelivered,
            PurchaseBusinessOutcome businessOutcome,
            string unknownOutcomeCode,
            string rawJson,
            HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            Undelivered = undelivered ?? s_none;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode;
            RawJson = rawJson;
            Error = error;
        }

        /// <summary>How the attempt ended.</summary>
        public FindUndeliveredStatus Status { get; }

        /// <summary>Which step did not complete, or <c>None</c> on success.</summary>
        public FindUndeliveredStep FailedStep { get; }

        /// <summary>
        /// Open purchases found at the server or store. Never null.
        /// Some entries require original order metadata before verification.
        /// An empty successful result means no purchases were found within this query's scope.
        /// On failure, inspect the outcome rather than treating the empty list as a successful query.
        /// </summary>
        public IReadOnlyList<PendingPurchase> Undelivered { get; }

        /// <summary>
        /// The translated business result. Meaningful only on
        /// <see cref="FindUndeliveredStatus.BusinessOutcome"/>.
        /// </summary>
        public PurchaseBusinessOutcome BusinessOutcome { get; }

        /// <summary>
        /// The server's Outcome code when this SDK build did not recognize it. Empty otherwise.
        /// Diagnostics only.
        /// </summary>
        public string UnknownOutcomeCode { get; }

        /// <summary>
        /// The unparsed response body, kept for forward-compatibility diagnosis only. Never branch on
        /// it and never show it to a player.
        /// </summary>
        public string RawJson { get; }

        /// <summary>
        /// The preserved SDK error. Non-null only on <see cref="FindUndeliveredStatus.Failure"/>.
        /// </summary>
        public HiveError Error { get; }

        /// <summary>The server answered, with however many open purchases it found.</summary>
        internal static FindUndeliveredOutcome Succeeded(
            IReadOnlyList<PendingPurchase> undelivered, string rawJson) =>
            new FindUndeliveredOutcome(
                FindUndeliveredStatus.Success, FindUndeliveredStep.None, undelivered,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, rawJson ?? string.Empty, null);

        /// <summary>The server answered with an Outcome this Recipe translates.</summary>
        internal static FindUndeliveredOutcome Business(
            FindUndeliveredStep failedStep, PurchaseBusinessOutcome businessOutcome, string rawJson) =>
            new FindUndeliveredOutcome(
                FindUndeliveredStatus.BusinessOutcome, failedStep, null,
                businessOutcome, string.Empty, rawJson ?? string.Empty, null);

        /// <summary>
        /// The server answered with an Outcome that is not translated — either unknown to this SDK
        /// build (<paramref name="unknownOutcomeCode"/> non-empty) or unmapped by this Recipe.
        /// </summary>
        internal static FindUndeliveredOutcome Unrecognized(
            FindUndeliveredStep failedStep, string unknownOutcomeCode, string rawJson) =>
            new FindUndeliveredOutcome(
                FindUndeliveredStatus.BusinessOutcome, failedStep, null,
                PurchaseBusinessOutcome.Unrecognized, unknownOutcomeCode ?? string.Empty,
                rawJson ?? string.Empty, null);

        /// <summary>A technical failure; the SDK error is preserved as given.</summary>
        internal static FindUndeliveredOutcome Failed(FindUndeliveredStep failedStep, HiveError error) =>
            new FindUndeliveredOutcome(
                FindUndeliveredStatus.Failure, failedStep, null,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);
    }
}

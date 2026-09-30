// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The result of preparing data for game-server verification.</summary>
    public enum SaveSubscriptionStatus
    {
        /// <summary>Data is ready for game-server verification; delivery is not authorized.</summary>
        Success,

        /// <summary>The service returned a business refusal. See BusinessOutcome.</summary>
        BusinessOutcome,

        /// <summary>A technical or input failure. See Error.</summary>
        Failure,
    }
    /// <summary>The preparation step that did not complete.</summary>
    public enum SaveSubscriptionStep
    {
        /// <summary>No step failed.</summary>
        None,

        /// <summary>Checking inputs and cancellation before service calls.</summary>
        Validate,

        /// <summary>Resolving required SDK services.</summary>
        Resolve,

        /// <summary>Saving subscription data.</summary>
        Save,
    }

    /// <summary>Preparation result. Success requires game-server verification and delivery before close.</summary>
    public sealed class SaveSubscriptionOutcome
    {
        private SaveSubscriptionOutcome(SaveSubscriptionStatus status, SaveSubscriptionStep failedStep,
            PendingPurchase pending, PurchaseBusinessOutcome businessOutcome,
            string unknownOutcomeCode, string rawJson, HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            Pending = pending;
            BusinessOutcome = businessOutcome;
            UnknownOutcomeCode = unknownOutcomeCode ?? string.Empty;
            RawJson = rawJson ?? string.Empty;
            Error = error;
        }

        /// <summary>The attempt result; read this before inspecting other fields.</summary>
        public SaveSubscriptionStatus Status { get; }
        /// <summary>None on success; otherwise the step that stopped preparation.</summary>
        public SaveSubscriptionStep FailedStep { get; }
        /// <summary>Retain for server verification or retry. Null when no input was supplied.</summary>
        public PendingPurchase Pending { get; }
        /// <summary>The service refusal when Status is BusinessOutcome; otherwise Unrecognized.</summary>
        public PurchaseBusinessOutcome BusinessOutcome { get; }
        /// <summary>The unrecognized service code, or an empty string when absent.</summary>
        public string UnknownOutcomeCode { get; }
        /// <summary>Diagnostics only. Never branch on this or show it to players.</summary>
        public string RawJson { get; }
        /// <summary>The preserved technical error when Status is Failure; otherwise null.</summary>
        public HiveError Error { get; }

        internal static SaveSubscriptionOutcome Succeeded(PendingPurchase pending) =>
            new SaveSubscriptionOutcome(SaveSubscriptionStatus.Success, SaveSubscriptionStep.None,
                pending, PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        internal static SaveSubscriptionOutcome Business(SaveSubscriptionStep step,
            PurchaseBusinessOutcome businessOutcome, string rawJson, PendingPurchase pending) =>
            new SaveSubscriptionOutcome(SaveSubscriptionStatus.BusinessOutcome, step, pending,
                businessOutcome, string.Empty, rawJson, null);

        internal static SaveSubscriptionOutcome Unrecognized(SaveSubscriptionStep step,
            string unknownOutcomeCode, string rawJson, PendingPurchase pending) =>
            new SaveSubscriptionOutcome(SaveSubscriptionStatus.BusinessOutcome, step, pending,
                PurchaseBusinessOutcome.Unrecognized, unknownOutcomeCode, rawJson, null);

        internal static SaveSubscriptionOutcome Failed(
            SaveSubscriptionStep step, HiveError error, PendingPurchase pending) =>
            new SaveSubscriptionOutcome(SaveSubscriptionStatus.Failure, step, pending,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);

    }
}

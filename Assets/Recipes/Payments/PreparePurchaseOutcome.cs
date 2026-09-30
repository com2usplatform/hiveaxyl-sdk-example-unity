// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The result of preparing data for game-server verification.</summary>
    public enum PreparePurchaseStatus
    {
        /// <summary>Data is ready for game-server verification; delivery is not authorized.</summary>
        Success,

        /// <summary>The service returned a business refusal. See BusinessOutcome.</summary>
        BusinessOutcome,

        /// <summary>A technical or input failure. See Error.</summary>
        Failure,
    }
    /// <summary>The preparation step that did not complete.</summary>
    public enum PreparePurchaseStep
    {
        /// <summary>No step failed.</summary>
        None,

        /// <summary>Checking inputs and cancellation before service calls.</summary>
        Validate,

        /// <summary>Resolving required SDK services.</summary>
        Resolve,

        /// <summary>Recovering a missing PG receipt from the server.</summary>
        Restore,

        /// <summary>Recording purchase data.</summary>
        Record,
    }

    /// <summary>Preparation result. Success requires game-server verification and delivery before close.</summary>
    public sealed class PreparePurchaseOutcome
    {
        private PreparePurchaseOutcome(PreparePurchaseStatus status, PreparePurchaseStep failedStep,
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
        public PreparePurchaseStatus Status { get; }
        /// <summary>None on success; otherwise the step that stopped preparation.</summary>
        public PreparePurchaseStep FailedStep { get; }
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

        internal static PreparePurchaseOutcome Succeeded(PendingPurchase pending) =>
            new PreparePurchaseOutcome(PreparePurchaseStatus.Success, PreparePurchaseStep.None,
                pending, PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, null);

        internal static PreparePurchaseOutcome Business(PreparePurchaseStep step,
            PurchaseBusinessOutcome businessOutcome, string rawJson, PendingPurchase pending) =>
            new PreparePurchaseOutcome(PreparePurchaseStatus.BusinessOutcome, step, pending,
                businessOutcome, string.Empty, rawJson, null);

        internal static PreparePurchaseOutcome Unrecognized(PreparePurchaseStep step,
            string unknownOutcomeCode, string rawJson, PendingPurchase pending) =>
            new PreparePurchaseOutcome(PreparePurchaseStatus.BusinessOutcome, step, pending,
                PurchaseBusinessOutcome.Unrecognized, unknownOutcomeCode, rawJson, null);

        internal static PreparePurchaseOutcome Failed(
            PreparePurchaseStep step, HiveError error, PendingPurchase pending) =>
            new PreparePurchaseOutcome(PreparePurchaseStatus.Failure, step, pending,
                PurchaseBusinessOutcome.Unrecognized, string.Empty, string.Empty, error);

        internal static PreparePurchaseOutcome Translate(
            PreparePurchaseStep step,
            SdkResultClassification classification,
            PurchaseBusinessOutcome businessOutcome,
            PendingPurchase pending)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UserCanceled:
                case SdkResultKind.UntypedProblem:
                    return Failed(
                        step,
                        classification.Problem ?? new HiveError(
                            HiveErrorCode.Cancelled, "The SDK reported the call as canceled."),
                        pending);

                case SdkResultKind.UnknownOutcome:
                    return Unrecognized(
                        step, classification.UnknownCode, classification.RawJson, pending);

                default:
                    return businessOutcome == PurchaseBusinessOutcome.Unrecognized
                        ? Unrecognized(step, string.Empty, classification.RawJson, pending)
                        : Business(step, businessOutcome, classification.RawJson, pending);
            }
        }
    }
}

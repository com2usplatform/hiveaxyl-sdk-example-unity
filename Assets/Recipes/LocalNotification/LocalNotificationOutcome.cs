// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>The state of one local-notification call.</summary>
    public enum LocalNotificationStatus
    {
        /// <summary>
        /// The call did what it was asked: the permission is granted, the notification is
        /// scheduled, or the cancel went through.
        /// </summary>
        Success,

        /// <summary>
        /// Notification permission was not granted. Nothing was scheduled.
        /// This status does not indicate whether another request can show a prompt.
        /// </summary>
        PermissionDenied,

        /// <summary>A technical failure. See <c>Error</c>.</summary>
        Failure,
    }

    /// <summary>Which step of the call did not complete.</summary>
    public enum LocalNotificationStep
    {
        /// <summary>Nothing failed.</summary>
        None,

        /// <summary>Checking what the caller supplied, before the OS is touched.</summary>
        Validate,

        /// <summary>Asking the OS for notification permission.</summary>
        Permission,

        /// <summary>Handing the notification to the OS scheduler.</summary>
        Schedule,

        /// <summary>Withdrawing a scheduled notification.</summary>
        Cancel,
    }

    /// <summary>
    /// The result of one local-notification call. Read <see cref="Status"/> first; the other
    /// members are meaningful per the state documented on each.
    /// </summary>
    /// <remarks>
    /// Reports success, permission not granted, or a technical failure.
    /// The permission API does not expose dialog dismissal as a separate result.
    /// Caller cancellation is reported as a failure with <see cref="HiveErrorCode.Cancelled"/>.
    /// </remarks>
    public sealed class LocalNotificationOutcome
    {
        private LocalNotificationOutcome(
            LocalNotificationStatus status, LocalNotificationStep failedStep, int id, HiveError error)
        {
            Status = status;
            FailedStep = failedStep;
            Id = id;
            Error = error;
        }

        /// <summary>How the call ended.</summary>
        public LocalNotificationStatus Status { get; }

        /// <summary>Which step did not complete, or <c>None</c> on success.</summary>
        public LocalNotificationStep FailedStep { get; }

        /// <summary>
        /// The scheduled notification's id, on a schedule <see cref="LocalNotificationStatus.Success"/>
        /// alone — what <see cref="LocalNotificationRecipe.Cancel"/> takes. Zero elsewhere.
        /// </summary>
        public int Id { get; }

        /// <summary>The preserved error, on <see cref="LocalNotificationStatus.Failure"/> alone.</summary>
        public HiveError Error { get; }

        internal static LocalNotificationOutcome Succeeded(int id = 0) =>
            new LocalNotificationOutcome(
                LocalNotificationStatus.Success, LocalNotificationStep.None, id, null);

        internal static LocalNotificationOutcome DeniedByPlayer() =>
            new LocalNotificationOutcome(
                LocalNotificationStatus.PermissionDenied, LocalNotificationStep.Permission, 0, null);

        internal static LocalNotificationOutcome Failed(LocalNotificationStep failedStep, HiveError error) =>
            new LocalNotificationOutcome(LocalNotificationStatus.Failure, failedStep, 0, error);
    }
}

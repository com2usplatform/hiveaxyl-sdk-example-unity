// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>How the OS answered the permission ask.</summary>
    public enum LocalNotificationPermissionStatus
    {
        /// <summary>The player allowed notifications.</summary>
        Granted,

        /// <summary>
        /// Notification permission was not granted.
        /// This status does not identify the user's action or whether another prompt is possible.
        /// </summary>
        Denied,

        /// <summary>The OS could not answer. See <see cref="LocalNotificationPermissionResult.Error"/>.</summary>
        Failure,
    }

    /// <summary>What the platform's permission ask came back with.</summary>
    public sealed class LocalNotificationPermissionResult
    {
        private LocalNotificationPermissionResult(LocalNotificationPermissionStatus status, HiveError error)
        {
            Status = status;
            Error = error;
        }

        /// <summary>How the ask ended.</summary>
        public LocalNotificationPermissionStatus Status { get; }

        /// <summary>The preserved error, on <see cref="LocalNotificationPermissionStatus.Failure"/> alone.</summary>
        public HiveError Error { get; }

        /// <summary>The player allowed notifications.</summary>
        public static LocalNotificationPermissionResult Granted() =>
            new LocalNotificationPermissionResult(LocalNotificationPermissionStatus.Granted, null);

        /// <summary>Notification permission was not granted.</summary>
        public static LocalNotificationPermissionResult Denied() =>
            new LocalNotificationPermissionResult(LocalNotificationPermissionStatus.Denied, null);

        /// <summary>
        /// The OS could not answer; the error is preserved as given. A failure reported without one
        /// gets an <see cref="HiveErrorCode.Internal"/> error in its place, so it never reads as a
        /// success.
        /// </summary>
        public static LocalNotificationPermissionResult Failed(HiveError error) =>
            new LocalNotificationPermissionResult(
                LocalNotificationPermissionStatus.Failure,
                error ?? new HiveError(
                    HiveErrorCode.Internal, "The OS failed the ask without saying why."));
    }

    /// <summary>What handing a notification to the OS scheduler came back with.</summary>
    public sealed class LocalNotificationScheduleResult
    {
        private LocalNotificationScheduleResult(int id, HiveError error)
        {
            Id = id;
            Error = error;
        }

        /// <summary>Whether the OS took the notification.</summary>
        public bool IsSuccess => Error == null;

        /// <summary>The id the notification is scheduled under, on success alone.</summary>
        public int Id { get; }

        /// <summary>The preserved error, on failure alone.</summary>
        public HiveError Error { get; }

        /// <summary>The OS took the notification under <paramref name="id"/>.</summary>
        public static LocalNotificationScheduleResult Scheduled(int id) =>
            new LocalNotificationScheduleResult(id, null);

        /// <summary>
        /// Reports scheduling failure. A missing error is replaced with <see cref="HiveErrorCode.Internal"/>.
        /// </summary>
        public static LocalNotificationScheduleResult Failed(HiveError error) =>
            new LocalNotificationScheduleResult(
                0,
                error ?? new HiveError(
                    HiveErrorCode.Internal, "The OS took no notification and gave no error."));
    }

    /// <summary>
    /// The platform's half of local notifications: the OS permission ask and the OS scheduler.
    /// Use <see cref="UnityLocalNotificationSource"/> for Unity's platform notification APIs on
    /// Android and iOS, and <see cref="AppleLocalNotificationSource"/> for the SDK's Apple
    /// local-notification add-on on macOS.
    /// </summary>
    /// <remarks>
    /// Every call is asynchronous because the add-on's scheduler is: its calls cross a native
    /// bridge and answer through a callback. A source over a synchronous API completes at once.
    /// </remarks>
    public interface ILocalNotificationSource
    {
        /// <summary>
        /// Checks or requests notification permission. Prompt behavior depends on the platform
        /// and permission state. Caller cancellation stops waiting; it does not close the OS dialog.
        /// </summary>
        Task<LocalNotificationPermissionResult> RequestPermissionAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Hands a validated request to the OS scheduler. A cancellation must not orphan a
        /// reservation the OS already holds: answer it as scheduled, with its id, or withdraw
        /// it before answering the cancellation.
        /// </summary>
        Task<LocalNotificationScheduleResult> ScheduleAsync(
            LocalNotificationRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Withdraws the scheduled notification with <paramref name="id"/>. Answers null when the
        /// cancel went through — an id that is not scheduled is a no-op, not an error — and the
        /// preserved error otherwise.
        /// </summary>
        Task<HiveError> CancelAsync(int id, CancellationToken cancellationToken);

        /// <summary>
        /// Withdraws scheduled notifications; null when it went through. The scope is the
        /// source's to define and document: the narrowest its OS API can honor, down to only
        /// what the source itself scheduled.
        /// </summary>
        Task<HiveError> CancelAllAsync(CancellationToken cancellationToken);
    }
}

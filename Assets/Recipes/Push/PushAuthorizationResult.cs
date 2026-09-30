// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>How the OS answered the permission ask.</summary>
    public enum PushAuthorizationStatus
    {
        /// <summary>The player allowed notifications.</summary>
        Granted,

        /// <summary>
        /// Notification permission was not granted.
        /// This status does not identify the user's action or whether another prompt is possible.
        /// </summary>
        Denied,

        /// <summary>The OS could not answer. See <see cref="PushAuthorizationResult.Error"/>.</summary>
        Failure,
    }

    /// <summary>What the platform's permission ask came back with.</summary>
    public sealed class PushAuthorizationResult
    {
        private PushAuthorizationResult(PushAuthorizationStatus status, HiveError error)
        {
            Status = status;
            Error = error;
        }

        /// <summary>How the ask ended.</summary>
        public PushAuthorizationStatus Status { get; }

        /// <summary>The preserved error, on <see cref="PushAuthorizationStatus.Failure"/> alone.</summary>
        public HiveError Error { get; }

        /// <summary>The player allowed notifications.</summary>
        public static PushAuthorizationResult Granted() =>
            new PushAuthorizationResult(PushAuthorizationStatus.Granted, null);

        /// <summary>Notification permission was not granted.</summary>
        public static PushAuthorizationResult Denied() =>
            new PushAuthorizationResult(PushAuthorizationStatus.Denied, null);

        /// <summary>The OS could not answer; the error is preserved as given.</summary>
        public static PushAuthorizationResult Failed(HiveError error) =>
            new PushAuthorizationResult(PushAuthorizationStatus.Failure, error);
    }
}

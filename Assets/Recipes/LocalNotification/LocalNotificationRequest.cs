// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>One notification to schedule: what it says and when it fires.</summary>
    public sealed class LocalNotificationRequest
    {
        /// <summary>Creates the request.</summary>
        /// <param name="title">The notification's title. Must not be blank.</param>
        /// <param name="body">The text under the title. May be empty.</param>
        /// <param name="fireAt">
        /// When the OS shows it. Must be in the future; local time unless its
        /// <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Utc"/>.
        /// </param>
        public LocalNotificationRequest(string title, string body, DateTime fireAt)
        {
            Title = title;
            Body = body ?? string.Empty;
            FireAt = fireAt;
        }

        /// <summary>The notification's title.</summary>
        public string Title { get; }

        /// <summary>The text under the title. Empty when there is none.</summary>
        public string Body { get; }

        /// <summary>When the OS shows it.</summary>
        public DateTime FireAt { get; }
    }
}

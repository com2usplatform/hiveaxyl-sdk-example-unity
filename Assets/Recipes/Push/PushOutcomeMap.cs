// Copyright (c) Com2uS Platform Corp. All rights reserved.

using Hive.Axyl.Push;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Maps SDK push outcomes to Recipe business outcomes.
    /// </summary>
    internal static class PushOutcomeMap
    {
        internal static PushBusinessOutcome Of(object result)
        {
            switch (result)
            {
                case PushUpsertTokenResult.ResourceNotInScope:
                    return PushBusinessOutcome.PushResourceNotInScope;

                case PushUpsertTokenResult.InvalidSubject:
                    return PushBusinessOutcome.PushInvalidSubject;

                default:
                    return PushBusinessOutcome.Unrecognized;
            }
        }
    }
}

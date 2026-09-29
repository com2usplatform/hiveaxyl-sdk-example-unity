// Copyright (c) Com2uS Platform Corp. All rights reserved.

#nullable enable

using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Outcome of a client-side provider token exchange (<see cref="ProviderTokenExchange"/>). On
    /// success it carries the <c>providerUserId</c> and the <c>providerToken</c> to hand to
    /// <c>IAuthService.LoginProviderAsync</c>; on failure it carries a typed <see cref="HiveError"/>.
    /// </summary>
    public sealed class ProviderTokenExchangeResult
    {
        private ProviderTokenExchangeResult(
            bool isSuccess, string? providerUserId, string? providerToken, HiveError? error)
        {
            IsSuccess = isSuccess;
            ProviderUserId = providerUserId;
            ProviderToken = providerToken;
            Error = error;
        }

        /// <summary><c>true</c> when the exchange produced a usable provider credential.</summary>
        public bool IsSuccess { get; }

        /// <summary>The provider's user id (Google: the id_token <c>sub</c>). Null on failure.</summary>
        public string? ProviderUserId { get; }

        /// <summary>The token to submit to the Axyl server (Google: the id_token). Null on failure.</summary>
        public string? ProviderToken { get; }

        /// <summary>The failure detail. Null on success.</summary>
        public HiveError? Error { get; }

        public static ProviderTokenExchangeResult Ok(string providerUserId, string providerToken)
            => new ProviderTokenExchangeResult(true, providerUserId, providerToken, null);

        public static ProviderTokenExchangeResult Fail(HiveError error)
            => new ProviderTokenExchangeResult(false, null, null, error);
    }
}

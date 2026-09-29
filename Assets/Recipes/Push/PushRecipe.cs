// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Core;
using Hive.Axyl.Push;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Prepares this device to receive push: asks the OS for permission, has the platform issue the
    /// device token, and registers it at the server.
    /// </summary>
    /// <remarks>
    /// Requires an initialized SDK with the platform push addon and an active session.
    /// Registration is device-scoped and is not removed by logout.
    /// Permission denial registers nothing and does not indicate whether another prompt is possible.
    /// Serialize registration attempts and retain token events received during a call.
    /// After success, repeat only when the latest token differs. Stop on failure or cancellation.
    /// Install APNs delegate forwarding before token lookup.
    /// </remarks>
    public sealed class PushRecipe
    {
        private readonly IPushTokenSource m_source;

        /// <summary>Creates the Recipe for one platform.</summary>
        /// <param name="source">
        /// The source used for notification permission and device tokens. Must not be null.
        /// Platform support and setup requirements depend on the source implementation.
        /// </param>
        public PushRecipe(IPushTokenSource source)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>
        /// Asks the OS for permission, issues the device token, and registers it at the server.
        /// Serialize calls. During registration, retain the latest token event and repeat after
        /// success only when it differs from the registered token. Stop on failure or cancellation.
        /// </summary>
        /// <param name="preparation">The registration's filters and consents. Must not be null.</param>
        /// <param name="cancellationToken">Cancels the calls.</param>
        public async Task<PreparePushOutcome> PrepareAsync(
            PushPreparation preparation, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return PreparePushOutcome.Failed(
                    PreparePushStep.Validate,
                    new HiveError(HiveErrorCode.Cancelled, "The caller canceled before the calls."));
            }

            var refused = Validate(preparation, out var language);
            if (refused != null)
            {
                return refused;
            }

            if (!HiveCore.TryResolve<IPushService>(out var push))
            {
                return PreparePushOutcome.Failed(
                    PreparePushStep.Resolve,
                    new HiveError(
                        HiveErrorCode.FailedPrecondition,
                        "The push Capability is not registered. Initialize the SDK with AddPush."));
            }

            var authorization = await m_source.RequestAuthorizationAsync(cancellationToken);

            // Re-checked after every await: a cancel does not reach into a call already in
            // flight, so a result can land after the caller stopped waiting — and it must not let
            // the preparation carry on, least of all to a success.
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(PreparePushStep.Authorize);
            }

            switch (authorization.Status)
            {
                case PushAuthorizationStatus.Granted:
                    break;

                case PushAuthorizationStatus.Denied:
                    // Permission was not granted. Do not register the token for notifications.
                    // This result does not identify the user's action or future prompt behavior.
                    return PreparePushOutcome.DeniedByPlayer();

                default:
                    return PreparePushOutcome.Failed(
                        PreparePushStep.Authorize,
                        authorization.Error ?? new HiveError(
                            HiveErrorCode.Internal, "The OS failed the ask without saying why."));
            }

            var token = await m_source.GetTokenAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(PreparePushStep.Token);
            }

            if (!token.IsSuccess)
            {
                return PreparePushOutcome.Failed(
                    PreparePushStep.Token,
                    token.Error ?? new HiveError(
                        HiveErrorCode.Internal, "The platform issued no token and no error."));
            }

            var registered = await push.UpsertTokenAsync(
                new UpsertTokenRequest
                {
                    Token = token.Token,
                    ProviderType = ToProviderType(token.Provider),
                    Language = language,
                    Country = preparation.Country,
                    TimezoneId = preparation.TimezoneId,
                    Agreement = new Agreement
                    {
                        Info = preparation.AgreedToInfo,
                        Advertise = preparation.AgreedToAdvertising,
                        Night = preparation.AgreedToNightAdvertising,
                    },
                    ServerId = preparation.ServerId,
                    AppVersion = preparation.AppVersion,
                },
                new ApiCallContext { Token = cancellationToken });

            // Even a registration that succeeded is not reported as one to a caller who canceled:
            // the upsert is idempotent, and the rerun re-registers the same token.
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(PreparePushStep.Register);
            }

            return TranslateRegister(registered, token);
        }

        // The refusals that need no call to discover. Answers null when the preparation is sound,
        // with the mapped language in the out parameter.
        private static PreparePushOutcome Validate(
            PushPreparation preparation, out LanguageCode language)
        {
            language = LanguageCode.Unspecified;

            if (preparation == null
                || string.IsNullOrWhiteSpace(preparation.Country)
                || string.IsNullOrWhiteSpace(preparation.TimezoneId))
            {
                return PreparePushOutcome.Failed(
                    PreparePushStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "Preparing for push needs a country and an IANA time zone — they are the "
                        + "campaign filters this registration exists for. Nothing was sent."));
            }

            if (!TryLanguage(preparation.Language, out language))
            {
                return PreparePushOutcome.Failed(
                    PreparePushStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        $"The registry names no message language \"{preparation.Language}\". "
                        + "Nothing was sent."));
            }

            // The registry refuses this shape, so it is refused here, before any call: nighttime
            // advertising is a subset of advertising, not a kind of its own.
            if (preparation.AgreedToNightAdvertising && !preparation.AgreedToAdvertising)
            {
                return PreparePushOutcome.Failed(
                    PreparePushStep.Validate,
                    new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "Nighttime advertising consent requires advertising consent. "
                        + "Nothing was sent."));
            }

            return null;
        }

        private static PreparePushOutcome TranslateRegister(
            PushUpsertTokenResult registered, PushTokenResult token)
        {
            var classification = SdkResultClassification.Of(registered);
            switch (classification.Kind)
            {
                case SdkResultKind.Success:
                    return PreparePushOutcome.Succeeded(token.Token, token.Provider);

                case SdkResultKind.UserCanceled:
                case SdkResultKind.UntypedProblem:
                    return PreparePushOutcome.Failed(
                        PreparePushStep.Register,
                        classification.Problem ?? new HiveError(
                            HiveErrorCode.Cancelled, "The SDK reported the call as canceled."));

                case SdkResultKind.UnknownOutcome:
                    return PreparePushOutcome.Unrecognized(
                        PreparePushStep.Register, classification.UnknownCode, classification.RawJson);

                default:
                    var outcome = PushOutcomeMap.Of(registered);
                    return outcome == PushBusinessOutcome.Unrecognized
                        ? PreparePushOutcome.Unrecognized(
                            PreparePushStep.Register, string.Empty, classification.RawJson)
                        : PreparePushOutcome.Business(
                            PreparePushStep.Register, outcome, classification.RawJson);
            }
        }

        private static PreparePushOutcome Canceled(PreparePushStep step) =>
            PreparePushOutcome.Failed(step, new HiveError(
                HiveErrorCode.Cancelled, "The caller canceled during the calls."));

        // Preserve the APNs environment reported by the source.
        private static UpsertTokenRequestProviderType ToProviderType(PushTokenProvider provider)
        {
            switch (provider)
            {
                case PushTokenProvider.Fcm: return UpsertTokenRequestProviderType.Fcm;
                case PushTokenProvider.ApnsSandbox: return UpsertTokenRequestProviderType.ApnsSandbox;
                default: return UpsertTokenRequestProviderType.Apns;
            }
        }

        // Accept defined language values other than Unspecified. Input that starts with a digit is
        // rejected before parsing.
        private static bool TryLanguage(string code, out LanguageCode language)
        {
            language = LanguageCode.Unspecified;
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            var normalized = code.Replace("-", string.Empty).Replace("_", string.Empty).Trim();
            if (normalized.Length == 0 || char.IsDigit(normalized[0]))
            {
                return false;
            }

            return Enum.TryParse(normalized, ignoreCase: true, out language)
                && language != LanguageCode.Unspecified
                && Enum.IsDefined(typeof(LanguageCode), language);
        }
    }
}

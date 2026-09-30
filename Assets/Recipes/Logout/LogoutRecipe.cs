// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Ends the current session: asks the server to sign the player out, then decides what happens to
    /// the session held on this device.
    /// </summary>
    /// <remarks>
    /// Requires initialized Core with <c>AddAuth</c> and an active session.
    /// Read <see cref="LogoutOutcome.SessionCleared"/> separately from the result status.
    /// Failure or cancellation does not necessarily mean the local session was cleared.
    /// A session replaced during logout is left untouched.
    /// The app owns stored credentials, navigation, and retry decisions.
    /// </remarks>
    public sealed class LogoutRecipe
    {
        /// <summary>
        /// Ends the current session.
        /// </summary>
        /// <param name="cancellationToken">
        /// The caller's cancellation token, forwarded to the SDK call. Cancelling it is reported as
        /// <c>Failure</c> with <see cref="HiveErrorCode.Cancelled"/>, whether the cancel landed
        /// before the call or while it was in flight.
        /// </param>
        /// <returns>
        /// The attempt's outcome. Act on <see cref="LogoutOutcome.SessionCleared"/> rather than on
        /// <see cref="LogoutOutcome.Status"/> alone.
        /// </returns>
        public async Task<LogoutOutcome> LogoutAsync(CancellationToken cancellationToken = default)
        {
            // Before the preconditions: a caller who already canceled gets Cancelled whatever else
            // is wrong, so the reason they are told matches the reason they stopped.
            if (cancellationToken.IsCancellationRequested)
            {
                return LogoutOutcome.Failed(LogoutStep.Logout, new HiveError(
                    HiveErrorCode.Cancelled,
                    "The caller canceled the logout before it started."), false);
            }

            if (!HiveCore.TryResolve<IAuthService>(out var auth) || auth == null)
            {
                return LogoutOutcome.Failed(LogoutStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "IAuthService is not registered. Initialize the SDK with AddAuth first."), false);
            }

            if (!HiveCore.TryResolve<ISessionManager>(out var session) || session == null)
            {
                return LogoutOutcome.Failed(LogoutStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "ISessionManager is not available. The SDK Core is not initialized."), false);
            }

            // Reported rather than treated as a no-op success. Success here would claim the server
            // was asked and agreed, and an app that shows "signed out" on a session it never had is
            // hiding its own bug. Nothing is cleared because nothing was live.
            if (!session.IsLoggedIn)
            {
                return LogoutOutcome.Failed(LogoutStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "No session is active, so there is nothing to log out of."), false);
            }

            // The session this logout is ending. Anything that arrives later is only allowed to
            // clear this one — see ClearIfUnchanged.
            var sessionAtStart = session.AccessToken;

            var result = await auth.LogoutPlayerAsync(new ApiCallContext { Token = cancellationToken });
            var classification = SdkResultClassification.Of(result);

            var serverEndedIt = classification.Kind == SdkResultKind.Success;

            // A cancel does not reach into the request already in flight, so the server may well
            // have finished the job. Either way the caller stopped waiting, and a result
            // arriving after that must not complete the call as a success.
            if (cancellationToken.IsCancellationRequested)
            {
                return LogoutOutcome.Failed(
                    LogoutStep.Logout,
                    new HiveError(HiveErrorCode.Cancelled, "The logout was canceled."),
                    // …but if the server did end the session, keeping it locally only buys a 401 on
                    // the next call. The outcome says canceled; this says what actually happened.
                    serverEndedIt && ClearIfUnchanged(session, sessionAtStart));
            }

            switch (classification.Kind)
            {
                case SdkResultKind.Success:
                    return ClearIfUnchanged(session, sessionAtStart)
                        ? LogoutOutcome.Succeeded()
                        : LogoutOutcome.SucceededWithSessionReplaced();

                case SdkResultKind.UnknownOutcome:
                    // The server answered with something; it just is not a code this build knows.
                    // That is still the server deciding, so the session stands.
                    return LogoutOutcome.Unrecognized(
                        classification.UnknownCode, classification.RawJson);

                case SdkResultKind.UserCanceled:
                case SdkResultKind.UntypedProblem:
                    // The call did not get through. The session stays: do not let a
                    // Recipe delete session state on a network failure or timeout alone, and a
                    // logout that could not be delivered has not happened. The app can retry.
                    return LogoutOutcome.Failed(
                        LogoutStep.Logout, classification.Problem, false);

                default:
                    var businessOutcome = Map(result);
                    return businessOutcome == LogoutBusinessOutcome.Unrecognized
                        ? LogoutOutcome.Unrecognized(string.Empty, classification.RawJson)
                        : LogoutOutcome.Business(businessOutcome, classification.RawJson);
            }
        }

        /// <summary>
        /// Clears the session only while it is still the one the logout started against, and reports
        /// whether it did.
        /// </summary>
        /// <remarks>
        /// A round trip is long enough for the app to sign in again, or for Core's own refresh to
        /// install a new pair. Clearing unconditionally would sign the player out of a session this
        /// call never asked about. Do not undo Core's own session handling.
        /// </remarks>
        private static bool ClearIfUnchanged(ISessionManager session, string sessionAtStart)
        {
            if (!string.Equals(session.AccessToken, sessionAtStart, StringComparison.Ordinal))
            {
                return false;
            }

            session.ClearSession();
            return true;
        }

        private static LogoutBusinessOutcome Map(AuthLogoutPlayerResult result)
        {
            switch (result)
            {
                case AuthLogoutPlayerResult.TerminateService _:
                    return LogoutBusinessOutcome.ServiceTerminated;
                case AuthLogoutPlayerResult.GuestSignoutBlocked _:
                    return LogoutBusinessOutcome.GuestSignoutBlocked;
                case AuthLogoutPlayerResult.AppIdMismatch _:
                    return LogoutBusinessOutcome.AppIdMismatch;
                case AuthLogoutPlayerResult.InvalidGatewayContext _:
                    return LogoutBusinessOutcome.InvalidGatewayContext;
                case AuthLogoutPlayerResult.AppNotFound _:
                    return LogoutBusinessOutcome.AppNotFound;
                case AuthLogoutPlayerResult.PlayerNotFound _:
                    return LogoutBusinessOutcome.PlayerNotFound;
                default:
                    return LogoutBusinessOutcome.Unrecognized;
            }
        }
    }
}

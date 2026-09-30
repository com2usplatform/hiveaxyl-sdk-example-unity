// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Hive.Axyl.Auth;
using Hive.Axyl.Core;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Signs a player in with a username and password, creating the account when there is not one
    /// yet, and leaves a live session behind either way.
    /// </summary>
    /// <remarks>
    /// Requires initialized Core with <c>AddAuth</c> and <c>AddToken</c>.
    /// This flow may create an account. Check <see cref="LoginWithUsernameOutcome.IsNewAccount"/>
    /// and inform the player, since a mistyped username can create an unintended account.
    /// Pass the password as entered; the Recipe hashes it before sending and does not log it.
    /// Supply a grant key from the game's server when the app requires one.
    /// A failed attempt leaves the existing session untouched. Cancellation does not undo account creation.
    /// </remarks>
    public sealed class UsernameLoginRecipe
    {
        private readonly string m_clientId;
        private readonly string m_deviceKey;

        /// <summary>
        /// Creates the Recipe with the identity the app was provisioned with.
        /// </summary>
        /// <param name="clientId">
        /// The PKCE client id this app logs in as. Must not be null or whitespace.
        /// </param>
        /// <param name="deviceKey">
        /// The app's device identifier. Keep it stable across launches. Must not be blank.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when either argument is null, empty, or whitespace.
        /// </exception>
        public UsernameLoginRecipe(string clientId, string deviceKey)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new ArgumentException(
                    "clientId must be a non-empty, non-whitespace string.", nameof(clientId));
            }

            if (string.IsNullOrWhiteSpace(deviceKey))
            {
                throw new ArgumentException(
                    "deviceKey must be a non-empty, non-whitespace string.", nameof(deviceKey));
            }

            m_clientId = clientId;
            m_deviceKey = deviceKey;
        }

        /// <summary>
        /// Signs the player in, creating the account first if there is not one yet.
        /// </summary>
        /// <param name="username">
        /// The name, exactly as the player typed it. Must not be null or whitespace.
        /// </param>
        /// <param name="password">
        /// The password, exactly as the player typed it. Must not be null or empty. It is hashed
        /// before it leaves this method.
        /// </param>
        /// <param name="grantKey">
        /// The key obtained from the game's server when required for account creation.
        /// Null, empty, and whitespace omit the key. Existing-account sign-in does not use it.
        /// </param>
        /// <param name="cancellationToken">
        /// The caller's cancellation token, forwarded to every SDK call.
        /// </param>
        /// <returns>
        /// The attempt's outcome. <see cref="LoginWithUsernameOutcome.IsNewAccount"/> says whether an
        /// account was created, which a screen should not keep to itself — it is also what a mistyped
        /// username looks like. A wrong password comes back as
        /// <see cref="LoginWithUsernameBusinessOutcome.UsernameOrPasswordIncorrect"/>.
        /// </returns>
        public async Task<LoginWithUsernameOutcome> LogInOrSignUpAsync(
            string username,
            string password,
            string grantKey = null,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return LoginWithUsernameOutcome.Failed(
                    LoginWithUsernameStep.LogIn, new HiveError(
                        HiveErrorCode.Cancelled, "The caller canceled the login before it started."));
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return LoginWithUsernameOutcome.Failed(
                    LoginWithUsernameStep.Validate, new HiveError(
                        HiveErrorCode.InvalidArgument,
                        "username must be a non-empty, non-whitespace string."));
            }

            // Reject empty passwords before hashing.
            if (string.IsNullOrEmpty(password))
            {
                return LoginWithUsernameOutcome.Failed(
                    LoginWithUsernameStep.Validate, new HiveError(
                        HiveErrorCode.InvalidArgument, "password must be a non-empty string."));
            }

            if (!HiveCore.TryResolve<IAuthService>(out var auth) || auth == null)
            {
                return LoginWithUsernameOutcome.Failed(LoginWithUsernameStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "IAuthService is not registered. Initialize the SDK with AddAuth first."));
            }

            if (!HiveCore.TryResolve<ITokenService>(out var tokens) || tokens == null)
            {
                return LoginWithUsernameOutcome.Failed(LoginWithUsernameStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "ITokenService is not registered. Initialize the SDK with AddToken first."));
            }

            if (!HiveCore.TryResolve<ISessionManager>(out var session) || session == null)
            {
                return LoginWithUsernameOutcome.Failed(LoginWithUsernameStep.Resolve, new HiveError(
                    HiveErrorCode.FailedPrecondition,
                    "ISessionManager is not available. The SDK Core is not initialized."));
            }

            // A verifier/challenge pair belongs to one call: the challenge goes out with it, the
            // verifier comes back to the token exchange for the code that call returned. A refused
            // call returns no code, so the fall-back below starts a fresh pair rather than reusing
            // one whose challenge has already been on the wire.
            var pkce = Pkce.Create();
            var passwordHash = PasswordHash.Sha256Hex(password);

            long playerId;
            string authorizationCode;
            var isNewAccount = false;
            var isBlocked = false;

            var loggedIn = await auth.LoginUsernameAsync(
                new UsernameLoginRequest
                {
                    Username = username,
                    Password = passwordHash,
                    ClientId = m_clientId,
                    CodeChallenge = pkce.Challenge,
                    CodeChallengeMethod = CodeChallengeMethod.S256,
                    DeviceKey = m_deviceKey,
                },
                new ApiCallContext { Token = cancellationToken });

            var loginClassification = SdkResultClassification.Of(loggedIn);

            if (loginClassification.Kind == SdkResultKind.Success
                && loggedIn is AuthLoginUsernameResult.Success loggedInOk)
            {
                playerId = loggedInOk.Data.PlayerId;
                authorizationCode = loggedInOk.Data.AuthorizationCode;
                isBlocked = loggedInOk.Data.IsBlock;
            }
            else if (loggedIn is AuthLoginUsernameResult.UsernameVerifyFailed)
            {
                // Attempt account creation only after UsernameVerifyFailed.
                pkce = Pkce.Create();

                var created = await auth.CreateUsernameAsync(
                    new UsernameCreateRequest
                    {
                        // Normalize blank optional keys to null.
                        GrantKey = string.IsNullOrWhiteSpace(grantKey) ? null : grantKey,
                        Username = username,
                        Password = passwordHash,
                        ClientId = m_clientId,
                        CodeChallenge = pkce.Challenge,
                        CodeChallengeMethod = CodeChallengeMethod.S256,
                        DeviceKey = m_deviceKey,
                    },
                    new ApiCallContext { Token = cancellationToken });

                var createClassification = SdkResultClassification.Of(created);

                if (createClassification.Kind == SdkResultKind.Success
                    && created is AuthCreateUsernameResult.Success createdOk)
                {
                    isNewAccount = true;
                    playerId = createdOk.Data.PlayerId;
                    authorizationCode = createdOk.Data.AuthorizationCode;
                }
                else if (created is AuthCreateUsernameResult.UsernameAlreadyExists)
                {
                    return LoginWithUsernameOutcome.Business(
                        LoginWithUsernameStep.LogIn,
                        LoginWithUsernameBusinessOutcome.UsernameOrPasswordIncorrect,
                        loginClassification.RawJson);
                }
                else
                {
                    return Translate(
                        LoginWithUsernameStep.SignUp, createClassification, MapCreateUsername(created));
                }
            }
            else
            {
                return Translate(
                    LoginWithUsernameStep.LogIn, loginClassification, MapLoginUsername(loggedIn));
            }

            var reachedBy = isNewAccount ? LoginWithUsernameStep.SignUp : LoginWithUsernameStep.LogIn;

            if (string.IsNullOrEmpty(authorizationCode))
            {
                return LoginWithUsernameOutcome.Failed(reachedBy, new HiveError(
                    HiveErrorCode.Internal,
                    "The login succeeded without an authorization code, so no token could be issued."));
            }

            var setup = await SessionSetup.EstablishAsync(
                tokens, session, m_clientId, authorizationCode, pkce.Verifier, playerId,
                cancellationToken);

            switch (setup.Status)
            {
                case SessionSetupStatus.Established:
                    return LoginWithUsernameOutcome.Succeeded(
                        playerId, isNewAccount, isBlocked);

                case SessionSetupStatus.TokenCallFailed:
                    return Translate(
                        LoginWithUsernameStep.SessionSetup, setup.Classification,
                        MapIssueToken(setup.TokenResult));

                case SessionSetupStatus.Cancelled:
                    return LoginWithUsernameOutcome.Failed(
                        LoginWithUsernameStep.SessionSetup,
                        new HiveError(
                            HiveErrorCode.Cancelled,
                            "The caller canceled before the session was installed."));

                case SessionSetupStatus.UnusableTokenResponse:
                    return LoginWithUsernameOutcome.Failed(
                        LoginWithUsernameStep.SessionSetup, setup.Problem);

                // Named rather than left to the arm below, because Problem is filled in for that
                // status alone. A status added later and missed here would otherwise arrive with a
                // null error and take the caller down on the first read of it.
                default:
                    return LoginWithUsernameOutcome.Failed(
                        LoginWithUsernameStep.SessionSetup,
                        new HiveError(
                            HiveErrorCode.Internal,
                            $"Unhandled session-setup status: {setup.Status}."));
            }
        }

        /// <summary>
        /// Turns a classified SDK result into this Recipe's outcome. Only the
        /// <see cref="SdkResultKind.TypedOutcome"/> branch consults
        /// <paramref name="businessOutcome"/>; the rest carry everything they need already.
        /// </summary>
        private static LoginWithUsernameOutcome Translate(
            LoginWithUsernameStep step,
            SdkResultClassification classification,
            LoginWithUsernameBusinessOutcome businessOutcome)
        {
            switch (classification.Kind)
            {
                case SdkResultKind.UntypedProblem:
                    return LoginWithUsernameOutcome.Failed(step, classification.Problem);

                case SdkResultKind.UnknownOutcome:
                    return LoginWithUsernameOutcome.Unrecognized(
                        step, classification.UnknownCode, classification.RawJson);

                default:
                    // A typed Outcome this Recipe has no value for stays unrecognized rather than
                    // being folded into a neighbouring meaning.
                    return businessOutcome == LoginWithUsernameBusinessOutcome.Unrecognized
                        ? LoginWithUsernameOutcome.Unrecognized(
                            step, string.Empty, classification.RawJson)
                        : LoginWithUsernameOutcome.Business(
                            step, businessOutcome, classification.RawJson);
            }
        }

        private static LoginWithUsernameBusinessOutcome MapCreateUsername(
            AuthCreateUsernameResult result)
        {
            switch (result)
            {
                case AuthCreateUsernameResult.InvalidGrantKey _:
                    return LoginWithUsernameBusinessOutcome.InvalidGrantKey;
                case AuthCreateUsernameResult.GrantRequiredMissing _:
                    return LoginWithUsernameBusinessOutcome.GrantKeyRequired;
                case AuthCreateUsernameResult.TerminateService _:
                    return LoginWithUsernameBusinessOutcome.ServiceTerminated;
                case AuthCreateUsernameResult.InvalidClientId _:
                    return LoginWithUsernameBusinessOutcome.InvalidClient;
                case AuthCreateUsernameResult.IpBlocked _:
                    return LoginWithUsernameBusinessOutcome.IpBlocked;
                case AuthCreateUsernameResult.ProviderConfigNotFound _:
                    return LoginWithUsernameBusinessOutcome.ProviderConfigNotFound;
                case AuthCreateUsernameResult.AppNotFound _:
                    return LoginWithUsernameBusinessOutcome.AppNotFound;
                default:
                    return LoginWithUsernameBusinessOutcome.Unrecognized;
            }
        }

        private static LoginWithUsernameBusinessOutcome MapLoginUsername(AuthLoginUsernameResult result)
        {
            switch (result)
            {
                case AuthLoginUsernameResult.UsernameVerifyFailed _:
                    return LoginWithUsernameBusinessOutcome.UsernameOrPasswordIncorrect;
                case AuthLoginUsernameResult.TerminateService _:
                    return LoginWithUsernameBusinessOutcome.ServiceTerminated;
                case AuthLoginUsernameResult.InvalidClientId _:
                    return LoginWithUsernameBusinessOutcome.InvalidClient;
                case AuthLoginUsernameResult.IpBlocked _:
                    return LoginWithUsernameBusinessOutcome.IpBlocked;
                case AuthLoginUsernameResult.AppNotFound _:
                    return LoginWithUsernameBusinessOutcome.AppNotFound;
                case AuthLoginUsernameResult.ProviderConfigNotFound _:
                    return LoginWithUsernameBusinessOutcome.ProviderConfigNotFound;
                default:
                    return LoginWithUsernameBusinessOutcome.Unrecognized;
            }
        }

        private static LoginWithUsernameBusinessOutcome MapIssueToken(TokenIssueTokenResult result)
        {
            switch (result)
            {
                case TokenIssueTokenResult.InvalidClient _:
                    return LoginWithUsernameBusinessOutcome.InvalidClient;
                case TokenIssueTokenResult.UnsupportedGrantType _:
                    return LoginWithUsernameBusinessOutcome.UnsupportedGrantType;
                case TokenIssueTokenResult.InvalidGrantExpired _:
                    return LoginWithUsernameBusinessOutcome.ExpiredAuthorizationCode;
                case TokenIssueTokenResult.InvalidGrantCodeChallenge _:
                    return LoginWithUsernameBusinessOutcome.CodeChallengeMismatch;
                case TokenIssueTokenResult.InvalidGrantRefreshToken _:
                    return LoginWithUsernameBusinessOutcome.InvalidRefreshToken;
                case TokenIssueTokenResult.InvalidGrant _:
                    // Checked after the more specific invalid_grant variants above.
                    return LoginWithUsernameBusinessOutcome.InvalidAuthorizationCode;
                case TokenIssueTokenResult.AppNotFound _:
                    return LoginWithUsernameBusinessOutcome.AppNotFound;
                case TokenIssueTokenResult.TemporarilyUnavailable _:
                    return LoginWithUsernameBusinessOutcome.TemporarilyUnavailable;
                default:
                    return LoginWithUsernameBusinessOutcome.Unrecognized;
            }
        }
    }
}

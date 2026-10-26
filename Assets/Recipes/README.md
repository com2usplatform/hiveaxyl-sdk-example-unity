# Recipes

UI-free reference code that coordinates SDK calls and returns typed outcomes.
Start with [RecipeExamples](../RecipeExamples/README.md) for application usage.
Copy and adapt the Recipes you need; they have no API compatibility promise across SDK releases.

## The Recipes at a glance

| Recipe | Entry points | Example |
| --- | --- | --- |
| Guest login | `LoginAsGuestAsync` | `GuestLoginExample` |
| Provider login | `LoginWithProviderAsync` | `ProviderLoginExample` |
| Auto login | `LoginAsync` | `AutoLoginExample` |
| Username login | `LogInOrSignUpAsync` | `UsernameLoginExample` |
| Custom login | `LoginWithCustomAsync` | `CustomLoginExample` |
| Link provider | `LinkProviderAsync` | `LinkProviderExample` |
| Logout | `LogoutAsync` | `LogoutExample` |
| Consumable purchase | `InitiatePurchaseAsync` → `PreparePurchaseAsync` → `ClosePurchaseAsync` | `ConsumablePurchaseExample` |
| Undelivered recovery | `FindUndeliveredAsync` → `PreparePurchaseAsync` → `ClosePurchaseAsync` | `UndeliveredRecoveryExample` |
| Subscription | `StartSubscriptionAsync` → `SaveSubscriptionAsync` → `ConfirmSubscriptionAsync` | `SubscriptionExample` |
| Push preparation | `PrepareAsync` | `PushPreparationExample` |
| Local notification | `RequestPermissionAsync` / `ScheduleAsync` / `CancelAsync` | `LocalNotificationExamples` |

Use `IAuthService.UnlinkProviderAsync` directly for unlinking; there is no unlink Recipe.
For provider and market selection, see [Adapters](../RecipeExamples/Adapters.md).

## Copying these into a game

Copy the selected folders with their `.asmdef` files and dependencies below. Always include
`Recipes.asmdef`, `AssemblyInfo.cs`, and `Helper/` for the shared `Hive.Axyl.Samples.Recipes` assembly.
Optional sources use `versionDefines` and `defineConstraints` to skip compilation when their
package is absent. Assemblies in your game that reference those sources need matching guards.

Keep Recipes free of UI and scene references. Logging and application decisions belong to callers.
Place optional addon references in separate source assemblies, not the shared `Recipes.asmdef`.

## Forced dependencies

All folders require the shared Recipes assembly and its Core/Auth dependencies.
Install the SDK packages and copy any additional Recipe folders named below.

| Folder | Assembly | Requires |
| --- | --- | --- |
| `GuestLogin/`, `AutoLogin/`, `UsernameLogin/`, `CustomLogin/`, `Logout/`, `Helper/` | `Hive.Axyl.Samples.Recipes` | Core, Auth |
| `ProviderLogin/` | `…Recipes.ProviderLogin` | Core, Auth — **no addon** |
| `LinkProvider/` | `…Recipes.LinkProvider` | Core, Auth — **no addon**. Also needs `ProviderLogin/`, whose credential-source contract it reuses |
| `ProviderLogin.Apple/` | `…ProviderLogin.Apple` | `com.com2usplatform.hiveaxyl.auth.addon.apple`. Also needs `ProviderLogin/` |
| `ProviderLogin.CredentialManager/` | `…ProviderLogin.CredentialManager` | `com.com2usplatform.hiveaxyl.auth.addon.credentialmanager` (Google, native Android). Also needs `ProviderLogin/` |
| `ProviderLogin.Gpg/` | `…ProviderLogin.Gpg` | `com.com2usplatform.hiveaxyl.auth.addon.gpg`. Also needs `ProviderLogin/` |
| `ProviderLogin.Steam/` | `…ProviderLogin.Steam` | `com.com2usplatform.hiveaxyl.auth.addon.steam`. Also needs `ProviderLogin/` |
| `ProviderLogin.WebAuth/` | `…ProviderLogin.WebAuth` | `com.com2usplatform.hiveaxyl.auth.addon.webauth` (Google, X, Steam, and relay-based Apple browser login) and `com.unity.nuget.newtonsoft-json`. Also needs `ProviderLogin/` |
| `Payments/` | `…Recipes.Payments` | Core, Payments — **no addon** |
| `Payments.Apple/` | `…Payments.Apple` | `com.com2usplatform.hiveaxyl.payments.addon.apple`. Also needs `Payments/` |
| `Payments.Google/` | `…Payments.Google` | `com.com2usplatform.hiveaxyl.payments.addon.google`. Also needs `Payments/` |
| `Payments.Steam/` | `…Payments.Steam` | Core, Payments; also needs `Payments/`. No addon compile dependency; `com.com2usplatform.hiveaxyl.payments.addon.steam` is required for `AddSteamMicrotransactions` and approval events. |
| `Payments.Pg/` | `…Payments.Pg` | Core, Payments — **no addon**. Also needs `Payments/` |
| `Push/` | `…Recipes.Push` | Core, Push — **no addon** |
| `Push.Apns/` | `…Push.Apns` | `com.com2usplatform.hiveaxyl.push.addon.apns`; on iOS also `com.unity.mobile.notifications` (the permission ask — without it the ask reports a precondition failure; macOS does not need it). Also needs `Push/`. Carries the APNs delegate forwarder (`.cs` + `.mm`, iOS plugins; macOS builds them into a bundle — [AppleDelegateForwarding.md](../RecipeExamples/AppleDelegateForwarding.md)) |
| `Push.Fcm/` | `…Push.Fcm` | `com.com2usplatform.hiveaxyl.push.addon.fcm`. Also needs `Push/` |
| `LocalNotification/` | `…Recipes.LocalNotification` | Core — **no addon**. `UnityLocalNotificationSource` (Android / iOS) additionally needs `com.unity.mobile.notifications` — a Unity registry package, not an SDK addon; the same `versionDefines` guard applies, to that one file |
| `LocalNotification.Apple/` | `…LocalNotification.Apple` | `com.com2usplatform.hiveaxyl.push.addon.applenotification` (macOS). Also needs `LocalNotification/`. Carries the local-notification delegate forwarder (`.cs` + `.mm`, built into a macOS bundle — [AppleDelegateForwarding.md](../RecipeExamples/AppleDelegateForwarding.md)) |

Credential Manager requires its SDK addon package and an Android OAuth client registered for your
app's package name and signing certificate. Pass the server's web client ID to the source.
Google Play Games is a separate provider.

## Apple browser sign-in

For relay-based browser sign-in, use `AppleWebCredentialSource` with a Services ID, an HTTPS relay in
`WebAuthOptions.RedirectUri`, and server-side code exchange. The relay must accept Apple's
`form_post` and forward `code`, `error`, and the unprefixed `state` to the app:

- Windows loopback uses the `<port>:<state>` relay convention.
- Other platforms pass `callbackUri` to the source and use `<callback-uri>|<state>`.
  The relay must allow that destination.

Both sources can be used on WebGL: choose `AppleWebCredentialSource` when a relay
accepts Apple POST responses and forwards to the game callback, or `AppleJsPopupCredentialSource`
when using Apple JS directly. The JS popup source is currently supported only in a Unity WebGL player.

For Apple JS popup sign-in, use `AppleJsPopupCredentialSource` in `ProviderLogin.Apple/`. The source only
requests authorization, exchanges the code with `IAuthService`, and maps the outcome.
Its `Internal/WebGL/` folder encapsulates the browser implementation:

- `AppleWebAuthorization` validates configuration, creates state and a SHA-256 nonce,
  and returns a parsed authorization result.
- `AppleWebPopupBridge` owns request correlation, cancellation, and the C#/JS boundary.
- `AppleWebSignIn.jslib` loads Apple JS from its CDN and calls `signIn` with `usePopup: true`.
- `AppleWebSignInParser` checks state and maps credentials, errors, and profile data.

Copy the entire `ProviderLogin.Apple/` folder, including the `.jslib`, together with
`ProviderLogin/`. The Apple addon supplies the credential and result types.
The popup uses `usePopup: true` to receive authorization responses through Apple JS,
including profile data requested with the `name` and `email` scopes.

Call `AppleJsPopupCredentialSource.Preload()` when the screen opens, and start `AcquireAsync`
from the player's click without awaiting other work first. Pass an HTTPS Return URL registered
on the Apple Services ID, with the same origin as the player's page. The sample fills in the
current origin's `/oauth/callback.html`; run it on an HTTPS domain and register that exact URL.
The popup returns its result through Apple JS, so no POST handler or callback page is needed
for this path. The same Return URL is passed to the server's code exchange.

Pass an empty scope to request neither name nor email. Caller cancellation remains
`Failure(Cancelled)`; dismissing the popup is `UserCanceled`. A newer attempt cancels the
previous managed wait; cancellation does not close Apple's window, and late results are ignored.

After authorization, `Profile` exposes the Recipe's `AppleWebGlProfile`, even if server exchange
fails. Preserve first-authorization profile data for a retry, but associate it with a
server-verified account before persisting it. Apple does not send the name on returning sign-ins.
The parser decodes identity-token claims only for profile hints; it does not validate a JWT.
The nonce hash is sent to Apple, matching the native Recipe's convention; the existing code
exchange contract does not accept a raw nonce, and this Recipe does not claim nonce verification.

## Steam browser sign-in

`SteamCredentialSource` needs the Steam client (Windows, macOS). Elsewhere, use `SteamOpenIdCredentialSource`.
Steam does not return to a custom scheme, so pass the relay URL built by `SteamRelayReturnTo.Build`
(with a fresh nonce per attempt) as `returnTo`, and the app callback as `redirectUri`:

- The app callback scheme is the Axyl app ID. On Android, register it as an intent-filter on the WebAuth
  addon's callback Activity, beside the OAuth redirect scheme. iOS needs no registration.
- On WebGL, Steam returns directly to the shipped callback page on the player's origin; no relay is used.

## Payment preparation contracts

`ConsumablePurchaseRecipe.PreparePurchaseAsync` validates and records a new purchase,
recovering a missing PG receipt when needed.
`UndeliveredPurchaseRecipe.PreparePurchaseAsync` records Apple/Google purchases;
for Steam/PG it validates the restored data locally.
`SubscriptionRecipe.SaveSubscriptionAsync` validates and saves subscription data. None of these verify payment.

After preparation, the game server verifies and grants once per transaction. Its delivery confirmation
is required before `ClosePurchaseAsync` or `ConfirmSubscriptionAsync`. The confirmation object is an
application contract, not proof of payment. See [server integration](../RecipeExamples/README.md#payment-server-integration).

Retain pending receipts and store finish tokens on failure or cancellation. For recovered purchases
missing price or currency, restore the original order metadata rather than guessing from today's catalog.

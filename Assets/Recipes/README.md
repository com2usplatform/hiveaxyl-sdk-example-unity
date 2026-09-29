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
| Local notification | `RequestPermissionAsync` / `ScheduleAsync` / `Cancel` | `LocalNotificationExamples` |

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
| `ProviderLogin.WebAuth/` | `…ProviderLogin.WebAuth` | `com.com2usplatform.hiveaxyl.auth.addon.webauth` (Apple, Google, X, and Steam browser login). Also needs `ProviderLogin/` |
| `Payments/` | `…Recipes.Payments` | Core, Payments — **no addon** |
| `Payments.Apple/` | `…Payments.Apple` | `com.com2usplatform.hiveaxyl.payments.addon.apple`. Also needs `Payments/` |
| `Payments.Google/` | `…Payments.Google` | `com.com2usplatform.hiveaxyl.payments.addon.google`. Also needs `Payments/` |
| `Payments.Steam/` | `…Payments.Steam` | Core, Payments; also needs `Payments/`. No addon compile dependency; `com.com2usplatform.hiveaxyl.payments.addon.steam` is required for `AddSteamMicrotransactions` and approval events. |
| `Payments.Pg/` | `…Payments.Pg` | Core, Payments — **no addon**. Also needs `Payments/` |
| `Push/` | `…Recipes.Push` | Core, Push — **no addon** |
| `Push.Apns/` | `…Push.Apns` | `com.com2usplatform.hiveaxyl.push.addon.apns`; on iOS also `com.unity.mobile.notifications` (the permission ask — without it the ask reports a precondition failure; macOS does not need it). Also needs `Push/` |
| `Push.Fcm/` | `…Push.Fcm` | `com.com2usplatform.hiveaxyl.push.addon.fcm`. Also needs `Push/` |
| `LocalNotification/` | `…Recipes.LocalNotification` | `com.unity.mobile.notifications` — a Unity registry package, not an SDK addon; the same `versionDefines` guard applies |

Credential Manager requires its SDK addon package and an Android OAuth client registered for your
app's package name and signing certificate. Pass the server's web client ID to the source.
Google Play Games is a separate provider.

## Apple browser sign-in

Use `AppleWebCredentialSource` with a Services ID, an HTTPS relay in `WebAuthOptions.RedirectUri`,
and server-side code exchange. The relay must accept Apple's `form_post` and forward `code`,
`error`, and the unprefixed `state` to the app:

- Windows loopback uses the `<port>:<state>` relay convention.
- Other platforms pass `callbackUri` to the source and use `<callback-uri>|<state>`.
  The relay must allow that destination.
- WebGL requires an HTTPS callback page on the player's origin that posts the result to the opener,
  such as `StreamingAssets/callback.html`. This page does not replace the HTTPS relay.

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

# Initialize once, then run a scenario

Install the Capability/addon packages your game uses, including their native artifacts.
Published SDK packages include their native artifacts.
Follow the installed addon's platform setup and verify native features in a player build.

Initialize on Unity's main thread before creating SDK-backed sources or subscribing to plugin events:

```csharp
using Hive.Axyl.Auth;
using Hive.Axyl.Core;
using Hive.Axyl.Core.Unity;

var config = CoreConfig.CreateBuilder(appId).Build();
HiveBootstrap.Initialize(config, builder =>
{
    builder.AddAuth().AddToken();
    // Add the selected provider, payment or push registrations from Adapters.md.
});
```

Import each extension method's module/addon namespace. Configuration values such as `appId` come from your app.
Capability registration uses production by default. Use `AddPayments(sandbox: true)` for payment sandbox,
or an explicit base URL when required. A sandbox overload exists only for capabilities that support it.

On supported players, WebAuth needs `ExternalUserAgentService.Register` after bootstrap, plus
`WindowsLoopbackService.Register` on Windows. Native Apple sign-in on macOS needs `AppleSignInService.Register`.
Follow the installed addon's instructions for other platforms; do not call unavailable native services in the Editor.

## Session ownership

Use the registered Axyl `clientId`, distinct from the provider's OAuth client ID.
Persist an app-managed `deviceKey` and reuse it across logins and restarts.
Keep tokens and guest credentials in secure, account/environment-scoped storage, not PlayerPrefs or logs.

If using the SDK's `com.com2usplatform.hiveaxyl.storage` package, call `builder.AddSecureStorage()`
during initialization. Check availability with `HiveCore.TryResolve<ISecureStorage>` afterwards.
It is unregistered in the Editor and on WebGL; use memory-only credentials or another secure store there.

The app persists the in-memory session needed for auto login:

```csharp
// After SDK initialization and successful login:
if (HiveCore.TryResolve<ISessionManager>(out var session) && session.IsLoggedIn)
{
    var saved = StoredSession.Create(
        session.AccessToken, session.RefreshToken, session.PlayerId);
    // APP: persist saved securely for this account/environment.
}
```

`StoredSession` properties are not serialized by `JsonUtility`. Save a DTO with public fields and
reconstruct it with `StoredSession.Create(...)` when loading.

- Save refreshed tokens on `OnSessionRefreshed`. On `OnSessionExpired`, clear only the matching saved session;
  the event also fires on explicit session clearing and carries no account information.
- Serialize saves with logout/account changes so an older save cannot restore a cleared session.
  Unsubscribe at teardown. A request failure alone does not mean the session expired.
- Persist returned guest credentials even when login fails after account creation; keep them separately
  from the saved session. Use logout's `SessionCleared` to decide saved-session cleanup.
- Check `IsBlocked` before gameplay. A null `AutoLoginOutcome.IsBlocked` requires a separate check
  if the game needs confirmed block status.

## Payments: prepare before the example

Sign in, initialize the store, and build `PurchaseOrder` from the catalog product and price the player accepted.
Supply currency, country, language and account context. Steam needs its account ID; Google subscriptions
need an eligible offer. Use `AccountUuid.Compute` when a derived player identifier is required.
`PurchaseMarkets.Available` lists build-target support, not plugin readiness.

Resolve `IPaymentsService` after SDK initialization:

| Market | Catalog API | Request |
| --- | --- | --- |
| Google | `FetchGoogleProductsAsync` | `ProductGoogle` |
| Apple | `FetchAppleProductsAsync` | `ProductApple` |
| Steam | `FetchSteamProductsAsync` | `ProductSteam` |
| PG | `FetchPgProductsAsync` | `ProductPg` |

For Google/Apple, get registered IDs from your catalog or `ListStoreProductIdsAsync`, query the store via
`IGooglePlayBillingPlugin.QueryProductDetailsAsync` / `IAppleStoreKitPlugin.GetProductsAsync`, and map
those details into `Products`. Build the order only after catalog success.

Implement [IPurchaseApplication and server delivery confirmation](README.md#payment-server-integration).
The required order is prepare → server verification and delivery → close. Retain pending data on failure.
For recovery, restore missing metadata from the original order, not a current catalog guess.

## Push and local notifications

FCM requires `google-services.json` and Android player setup. APNs requires signing/entitlements and
its native delegate-forwarder template. Local notifications require Unity Mobile Notifications and
Android channel metadata, but no SDK initialization; the source supports Android/iOS players.

Subscribe to notification events for reception/navigation. One application owner should read the single-use
startup buffer and route the result when navigation is ready:

- APNs: await the forwarder's `ColdStartRelayed`, then call `IAPNSPlugin.GetColdStartNotificationAsync`.
  Check `Data.Notification.UserInfoJson` for content.
- FCM: call `IFCMPlugin.GetColdStartMessageAsync` and check `Data.Message.MessageId`.

Normal launches may return objects with empty fields. Subscribe to `TokenRefreshed` and serialize registration:
retain the latest token during preparation, then repeat after success only if it differs from the registered token.
Stop on failure/cancellation and unregister handlers at teardown.

On iOS, bind the examples' `restoreNotificationDelegate` to the installed forwarder's `Install` entry point.
They call it in `finally` after permission requests, including failure/cancellation, because Unity can replace
that delegate. Pass `() => { }` only where the integration has no such forwarder.

## Shutdown

Stop new work, cancel and await outstanding operations, remove event handlers, and dispose owned sources/listeners.
Persist recoverable transactions before calling `HiveCore.Shutdown()` at application shutdown.
Closing a UI screen should not shut down a shared SDK.

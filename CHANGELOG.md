# Changelog

Changes to the Recipes and the examples that call them are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

An entry earns its place here by customer impact at ship time: a change with no effect
on the Recipes or on the behaviour they ship does not belong here.

## [2.0.0] - 2026-10-26

Verified against Hive Axyl SDK for Unity 1.0.1.

### Added

- Local notifications on macOS through the SDK's Apple local-notification add-on:
  `AppleLocalNotificationSource` (`Assets/Recipes/LocalNotification.Apple/`) is the
  Recipe's platform half there, the way `UnityLocalNotificationSource` is on Android and
  iOS. Cancel-all withdraws what the Recipe scheduled and leaves the game's other
  notifications alone — on Android and iOS it stays app-wide, the narrowest cancel
  Unity Mobile Notifications offers.
- `Recipes/Push.Apns` and `Recipes/LocalNotification.Apple` carry the Apple delegate
  forwarders, C# and Objective-C++ sources together, that wire the OS notification
  callbacks into each add-on's `Notify*` methods.

### Changed

- `ILocalNotificationSource` and `LocalNotificationRecipe` schedule and cancel
  asynchronously: `ScheduleAsync`, `CancelAsync`, `CancelAllAsync` replace `Schedule`,
  `Cancel`, `CancelAll`. The add-on answers through a native bridge, so a synchronous
  source could not sit behind the interface. A source over an immediate API, such as
  `UnityLocalNotificationSource`, wraps its answer; its synchronous calls remain.
  A cancellation that lands after the OS placed the reservation answers as the success
  it is, id included — never as a cancellation that would orphan the notification.
- `Assets/Recipes/LocalNotification/` no longer needs `com.unity.mobile.notifications` as
  a whole — only `UnityLocalNotificationSource.cs` does — so a macOS-only project keeps
  the Recipe and its interface.

### Fixed

- WebGL X and Google sign-in failed at the provider: `WebAuthOptions.WebGlCallbackUri()`
  named `callback.html` under the player's own `StreamingAssets/` folder, a page that moves
  with every deployment, so the exact-match redirect URI could not be registered ahead of
  time. It now names `/oauth/callback.html` on the page's origin; host a copy of
  `StreamingAssets/callback.html` at that fixed path and register the one URL.
  `scripts/serve-webgl.py` serves the build's copy at that path for the local check.
- WebGL Apple sign-in supports the `name` and `email` scopes through
  `AppleJsPopupCredentialSource`, using Apple JS with `usePopup: true` to receive the
  authorization response without a POST callback handler. It exchanges the authorization
  code on the server and exposes the returned profile through `AppleWebGlProfile`.
  Register the HTTPS origin and Return URL on the Apple Services ID, and call
  `AppleJsPopupCredentialSource.Preload()` before the sign-in click. Games with a POST
  relay can use `AppleWebCredentialSource`, including in WebGL.

## [1.0.0] - 2026-09-29

First release. There is no earlier version to compare against, so this entry says what
the release contains rather than what changed; each Recipe's behaviour is documented
where the Recipe is, in [`Assets/Recipes/README.md`](Assets/Recipes/README.md) and in
the example that calls it.

### Added

- Recipes for authentication: guest, auto login, username, custom login against the
  game's own server, provider login, provider linking, and logout. Provider credential
  sources ship for Apple, Google Play Games, Steam, Android Credential Manager, and the
  browser-based WebAuth flow.
- Steam signs in through its own OpenID 2.0 page where there is no Steam client. Steam
  refuses to return to a custom scheme, so the return URL is the Axyl relay, which
  redirects to the app callback's scheme — the Axyl app id, not the store package name.
  On Android that scheme must be declared as an intent-filter on the WebAuth addon's
  callback Activity, or the relay's redirect reaches no Activity and the sign-in never
  comes back; iOS needs no registration.
- Recipes for payments: consumable purchase, subscription, and recovery of purchases
  the store completed but the game never finished. Market sources ship for Apple,
  Google Play, Steam, and the PG web flow.
- Recipes for push and local notifications, with token sources for FCM and APNs.
- [`Assets/RecipeExamples/`](Assets/RecipeExamples/README.md) — one short, UI-free
  walkthrough per Recipe, marking with `// APP:` each decision the Recipe leaves to the
  application, and [`Initialization.md`](Assets/RecipeExamples/Initialization.md) for
  where SDK initialization belongs relative to them.

Recipes are reference code to copy and adapt, not a library to depend on. There is no
API compatibility promise: a Recipe is correct for the SDK release it was verified
against and may change with the next one.

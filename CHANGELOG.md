# Changelog

Changes to the Recipes and the examples that call them are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

An entry earns its place here by customer impact at ship time: a change with no effect
on the Recipes or on the behaviour they ship does not belong here.

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

# Select an adapter

Install the [required packages](../Recipes/README.md#forced-dependencies) and complete
[initialization](Initialization.md) first. Then create the source you need with `new` and pass
it to the example. Creating a source does not initialize the SDK or register its plugins.
All source types below are in `Hive.Axyl.Samples.Recipes`; configuration values come from your app.

## Provider login and account linking

Pass an `IProviderCredentialSource` to `ProviderLoginExample.RunAsync` or `LinkProviderExample.RunAsync`.

| Route | Create the source | Required setup |
| --- | --- | --- |
| Apple native | `new AppleCredentialSource(new AppleSignInOptions { RequestEmail = true })` | AddAppleSignIn; Apple app/signing configuration |
| Apple browser | `new AppleWebCredentialSource(webOptions, callbackUri)` | AddWebAuth; Apple Services ID; HTTPS relay |
| Google native | `new GoogleCredentialManagerCredentialSource(webClientId)` | AddCredentialManager; Android package/signing configuration; server web client ID |
| Google browser | `new GoogleCredentialSource(webOptions)` | AddWebAuth; OAuth registration and callback |
| Google Play Games | `new GooglePlayGamesCredentialSource(webClientId)` | AddAuth + AddGooglePlayGames; Android Play Games configuration; server web client ID |
| X browser | `new XCredentialSource(webOptions)` | AddWebAuth; OAuth registration and callback |
| Steam native | `new SteamCredentialSource(identity, steamId)` | AddSteamAuth; initialized Steamworks with callback pumping; backend ticket identity |
| Steam browser | `new SteamOpenIdCredentialSource(returnTo, redirectUri)` | AddWebAuth; HTTP(S) return-to URL and app callback |

Create `webOptions` with `WebAuthOptions.Create(authorizeEndpoint, providerClientId, redirectUri, scope)`
using your provider's registered values.

- **Apple browser:** `webOptions.RedirectUri` is the HTTPS relay; `callbackUri` is the app callback
  the relay forwards to (omit for Windows loopback). Server-side exchange is required.
  See the [Apple browser setup](../Recipes/README.md#apple-browser-sign-in) for relay and WebGL requirements.
- **Steam browser:** `returnTo` is the URL Steam returns to; `redirectUri` is the callback captured
  by the app. If they are the same, use `new SteamOpenIdCredentialSource(returnTo)`.
  See the [Steam browser setup](../Recipes/README.md#steam-browser-sign-in) for the relay and Android registration.

## Purchases and subscriptions

Pass an `IStorePurchaseSource` to the purchase or recovery example.

| Market | Create the source | Required setup |
| --- | --- | --- |
| Google | `new GooglePurchaseSource()` | AddPayments + AddPlayBilling; Play catalog/account setup |
| Apple | `new ApplePurchaseSource()` | AddPayments + AddStoreKit; catalog and signed StoreKit setup |
| Steam | `new SteamPurchaseSource()` | AddPayments + AddSteamMicrotransactions; initialized Steamworks; approval events |
| PG | `new PgPurchaseSource(quantity)` | AddPayments; payment page opening and return handling |

For `SubscriptionExample`, use Apple or Google: only these sources implement `ISubscriptionSource`.
Prepare the [catalog and order](Initialization.md#payments-prepare-before-the-example) before purchasing.
Your game server must [verify and confirm delivery](README.md#payment-server-integration) before closing.

## Push and local notifications

| Feature | Create the source | Required setup |
| --- | --- | --- |
| FCM | `new FcmPushTokenSource()` | AddPush + AddFCM; Firebase configuration; Android permissions |
| APNs | `new ApnsPushTokenSource()` | AddPush + AddAPNS; signing/entitlements; native delegate forwarding |
| Local notifications | `new UnityLocalNotificationSource(channelId, channelName, description)` | Unity Mobile Notifications; Android channel metadata; no SDK initialization |

Call `PushPreparationExample.RunAsync` with the push source, a `PushPreparation`, and
`restoreNotificationDelegate` (a no-op where delegate restoration is not needed). Use
`LocalNotificationExamples` for local notifications. Dispose push sources after pending work stops. Follow the
[notification setup](Initialization.md#push-and-local-notifications) for event ownership and iOS delegate restoration.

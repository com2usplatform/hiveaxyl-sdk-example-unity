# Apple delegate forwarding

The APNs add-on (iOS / macOS) and the Apple local-notification add-on (macOS) install no
app delegate: the app owns its `UIApplicationDelegate` / `UNUserNotificationCenterDelegate`
and forwards the OS callbacks into each add-on's `Notify*` methods. Each add-on's README
states that contract — which callback feeds which call. Unity owns the single app delegate
and exposes none of those selectors to managed code, so the Recipes carry the code that does
the forwarding:

| Recipe folder | Files | Wires |
| --- | --- | --- |
| `Recipes/Push.Apns/` | `ApnsRemoteNotificationForwarder.mm` (native: token path, remote-notification relay, content → JSON), `ApnsRemoteNotificationForwarder.cs` (managed: callbacks → `IAPNSPlugin.Notify*`), `ApnsColdStartCapture.mm` (native, cold start only: a `+load` observer captures the launching notification for `Install` to relay) | `IAPNSPlugin` |
| `Recipes/LocalNotification.Apple/` | `LocalNotificationAppleForwarder.mm` (native: local-notification relay, content → JSON), `LocalNotificationAppleForwarder.cs` (managed: callbacks → `ILocalNotificationApplePlugin.Notify*`) | `ILocalNotificationApplePlugin` |

Copy the Recipe folder you use as it is, native source included, and change the namespace at
the top of each `.cs` to your own. A project that uses one add-on copies one folder; the other
add-on's code is never in it.

## Placement

### iOS (APNs)

Nothing to configure. Both `.mm` files are iOS plugins, so Unity links them into the Xcode
project as `__Internal`; the capturer's `+load` runs before launch completes, so cold start
works out of the box. The local-notification add-on is macOS-only, so its forwarder has no
iOS part.

### macOS (standalone)

The macOS standalone player does not compile loose native source, so each forwarder is
supplied as a **prebuilt bundle** built from its `.mm`. Build it, put it under
`Assets/Plugins/macOS/` (or any folder; the importer settings decide), and keep `k_NativeLib`
in the `.cs` at its default — the bundle name. `Install()` wires the forwarding at runtime,
so it does not matter when the bundle loads: a bare `.app` build works too.

```sh
# adjust architectures / SDK to the project; same command for LocalNotificationAppleForwarder
clang++ -bundle -framework Foundation -framework AppKit \
  -framework UserNotifications -fobjc-arc \
  -arch x86_64 -arch arm64 \
  ApnsRemoteNotificationForwarder.mm \
  -o ApnsRemoteNotificationForwarder.bundle/Contents/MacOS/ApnsRemoteNotificationForwarder
```

`Contents/Info.plist` inside the `.bundle` is standard bundle metadata (`CFBundleExecutable`
= the executable name). Rebuild the bundle whenever the `.mm` changes — the player loads the
binary, not the source.

**Cold start on macOS (APNs, optional).** The capturer's `+load` observer must run before
launch completes, which a lazily `dlopen`ed bundle (loaded at the first P/Invoke, after
launch) cannot do. So `ApnsColdStartCapture.mm` is compiled **into the app executable**: the
bundle's `Install()` reads what it captured through the `AxylApnsColdStartTake` export
(resolved with `dlsym`) and chains its own delegate in front of the capturer's. Do not
compile the forwarder `.mm` into the app as well — its Objective-C classes would be
registered twice (app + bundle). Build with Build Settings → macOS → **Create Xcode
Project**, then in the generated app target add `ApnsColdStartCapture.mm` under **Build
Phases → Compile Sources** and link `AppKit.framework` and `UserNotifications.framework`
under **General → Frameworks and Libraries**. Unity drops these edits on a **Replace**
build; a `PostProcessBuild` callback with `PBXProject` makes them stick. A bare `.app` has
no capturer: cold start is silently unavailable while `Notify*` (the bundle) keeps working.

The capturer takes `UNUserNotificationCenter.delegate` at launch; any delegate installed
later must **chain** the existing one as `previous` (as these forwarders do). Code that
replaces `center.delegate` after launch without chaining drops the capturer from the chain,
and cold start is not supported in that configuration.

## Wiring

Call `Install` after the add-on instance is available (after `HiveBootstrap.Initialize`):

```csharp
var apns = HiveCore.Resolve<IAPNSPlugin>();
ApnsRemoteNotificationForwarder.Install(apns);

var local = HiveCore.Resolve<ILocalNotificationApplePlugin>();   // macOS
LocalNotificationAppleForwarder.Install(local);
```

Calling `Install` again is harmless: callbacks go to the plugin passed to the latest call. If
the APNs forwarder's delegate is still in the chain nothing happens; if some other code
replaced `center.delegate` wholesale, `Install` keeps that delegate as `previous` and
reinstalls in front of it. The local-notification forwarder is macOS-only, has no such
reinstall path, and does nothing after its first `Install`. `GetTokenAsync` after `Install`
starts APNs registration and completes as soon as the token arrives.

> **Using `com.unity.mobile.notifications` too?** On iOS that package replaces
> `UNUserNotificationCenter.delegate` with its own — at app start and **again on every
> permission request** (`AuthorizationRequest`, `NotificationCenter.RequestPermission`) —
> without keeping the previous one. The start-up replacement is fine (`Install` runs later
> and chains it). The first permission request that shows the OS prompt, however, knocks the
> forwarder out, and from then on remote `NotificationPresented` / `NotificationOpened` stop
> reaching the add-on. Notification permission is app-wide, so that first request may come
> from a local-notification screen rather than a push screen. **Wherever it happens, call
> `ApnsRemoteNotificationForwarder.Install` once more right after the first permission
> request completes.** It is a no-op while the forwarder is intact, so calling it after
> every permission request costs nothing — the examples' `restoreNotificationDelegate` is
> this call.

**Foreground presentation is the app's decision.** The APNs `.mm` answers a remote
notification's `willPresent` itself with `banner | list | sound | badge` instead of passing
it down the chain — so that a delegate chained behind it (e.g. Mobile Notifications) cannot
answer `None` for a notification it does not own and erase the banner. To present
differently (silent, hidden), edit the `UNNotificationPresentationOptions` handed to the
`willPresent` completion handler (`kAxylApnsForegroundOptions`). The `Notify*` call only
raises `NotificationPresented`; changing the options does not affect the event.

### Cold-start notification

When a notification tap launches the app from a terminated state, the capturer grabs it and
the forwarder relays it through `NotifyColdStartNotificationAsync`. At start-up, **await
`ColdStartRelayed`, then read it once**. `Install()` only schedules the relay and returns;
this task is the one signal that this launch's cold-start state is settled:

```csharp
await ApnsRemoteNotificationForwarder.ColdStartRelayed;
var result = await apns.GetColdStartNotificationAsync();
if (result is ApnsServiceGetColdStartNotificationResult.Success ok
    && !string.IsNullOrEmpty(ok.Data.Notification.UserInfoJson))
{
    var launching = ok.Data.Notification; // the notification that launched the app
}
```

`UserInfoJson` is the unset check: the response's `Notification` is never null, a launch
without a notification returns it with every field at its default, and a relayed payload
always fills `UserInfoJson`. Unset also comes back on the second read (single use), when the
capturer is not in the build (macOS bare `.app`), and when read without awaiting
`ColdStartRelayed`.

## Using both add-ons

A process has one `UNUserNotificationCenterDelegate`, and it receives remote and local
notifications alike. The two forwarders each install their own delegate and keep whatever
was set before as `previous`. Each relays only its own notifications — the APNs one those
with a `UNPushNotificationTrigger`, the local one everything else — and hands the rest down
the chain, so `Install` order does not matter. Copy both folders, call both `Install`s, and
the two add-ons share one chain without interfering. Without that trigger-based split, local
notifications would surface as the APNs add-on's events.

A delegate the game installed itself keeps working as `previous` too; each forwarder relays
first, so the game's delegate cannot block the relay. One exception: remote `willPresent` is
answered by the APNs forwarder and not passed on (see above), so the game delegate's
`willPresent` is not called for remote notifications. Taps and local notifications follow
the chain as before.

**Where the trigger split ends.** "No push trigger" also matches local notifications the
game scheduled itself, outside the add-on, so those flow into the local forwarder's relay as
well. A game that needs to tell the two apart checks `notification.request.identifier` (its
own prefix, or a set of the ids it scheduled) and ANDs that check onto the relay condition
(`!AxylLocalIsRemote(...)`) in `LocalNotificationAppleForwarder.mm`.

## Keeping the JSON mapping in sync

`AxylApnsNotificationJson` and `AxylLocalPayloadJson` in the `.mm` files mirror, by hand,
the mapping each add-on's native side applies to `UNNotificationContent`. When a new add-on
version adds or changes a payload field, update the mapper too — otherwise the managed DTO
(`ApnsNotification`; `NotificationPayload` / `NotificationContent`) silently misses the field
(no compile error; it only shows at runtime). Check: the DTO's properties equal the keys the
mapper writes. Put that comparison on your add-on-upgrade checklist, and rebuild the macOS
bundle after any `.mm` change.

## Verifying

In the Unity Editor and on non-Apple targets the native plugin is not linked: `Install` is a
no-op and the calls report Failure. Verify APNs on an **iOS device or a macOS build**, local
notifications in a **macOS build** (on iOS,
`HiveCore.Resolve<ILocalNotificationApplePlugin>()` throws `RegistrationNotFoundException`):

1. Grant permission (`RequestAuthorizationAsync` / `RequestPermissionAsync`).
2. APNs: `GetTokenAsync` returns a token and `TokenRefreshed` fires.
3. Send a remote notification, or schedule a local one: `NotificationPresented` in the
   foreground, `NotificationOpened` on tap.
4. (Both add-ons) Send a remote and a local notification and confirm neither surfaces as the
   other's event.

Handling of action buttons themselves (what the game does when one is pressed) is outside the
add-on; the `actionIdentifier` is delivered on the event.

## Time-sensitive notifications

Setting a notification's interruption level to `TIME_SENSITIVE` lets it break through Focus
/ Do Not Disturb — but only when the app declares the
`com.apple.developer.usernotifications.time-sensitive` entitlement itself. Undeclared, the OS
silently demotes such notifications to `passive`. Unlike `aps-environment`, the add-ons do
not inject this entitlement: whether to use time-sensitive delivery is the game's policy, and
it must match a provisioning profile whose App ID has the **Time Sensitive Notifications**
capability enabled.

1. Enable **Time Sensitive Notifications** on the App ID in the Apple Developer Portal and
   reissue the provisioning profile.
2. Add the key to the app's entitlements file — the same file the APNs add-on writes
   `aps-environment` into, from a post-process callback ordered after the add-on's:

   ```xml
   <key>com.apple.developer.usernotifications.time-sensitive</key>
   <true/>
   ```

`CRITICAL` needs the separate Critical Alerts entitlement and Apple's approval; this section
covers time-sensitive only. The entitlement changes delivery priority, not presentation —
foreground display is still decided by the presentation options above.

// Copyright (c) Com2uS Platform Corp. All rights reserved.
//
// STANDARD DELEGATE TEMPLATE — APNs (remote push) only.
//
// The APNs addon installs no app delegate by design: the app owns its
// `UIApplicationDelegate` /
// `UNUserNotificationCenterDelegate`, and forwards the delegate callbacks into
// the addon's game-driven `Notify*` RPCs. Unity owns the single app delegate and
// exposes no managed hook for these selectors, so this file installs the
// forwarding for you. Copy it into your project's Apple native plugins folder
// (iOS compiles it via `__Internal`; macOS standalone loads it as a prebuilt
// bundle — see AppleDelegateForwarding.md) and wire ApnsRemoteNotificationForwarder.cs.
//
// What it does:
//   * Swizzles `didRegisterForRemoteNotificationsWithDeviceToken:` /
//     `didFailToRegisterForRemoteNotificationsWithError:` onto the running app
//     delegate's class, chaining any original IMP so existing behaviour survives.
//     The device token is relayed as a lowercase hex string.
//   * Installs a `UNUserNotificationCenterDelegate` (Unity installs none) that
//     relays a REMOTE notification's willPresent / didReceive into the addon,
//     and returns the foreground presentation options.
//   * Relays the cold-start tap — the notification that launched a terminated app — into
//     NotifyColdStartNotification so GetColdStartNotification can return it. The LAUNCH is
//     captured by a companion file, ApnsColdStartCapture.mm, whose +load observer runs
//     before the app finishes launching; Install reads that captured payload here via the
//     AxylApnsColdStartTake export (resolved with dlsym). The capture is a SEPARATE file
//     because it must load before launch: on iOS both files link statically (__Internal),
//     but on macOS this forwarder ships as a prebuilt bundle for Notify* (loaded too late
//     to catch a launch), so only ApnsColdStartCapture.mm is compiled INTO the app — and
//     the bundle's Install still reaches its payload across the link boundary via dlsym. A
//     macOS build without that compiled-in capturer (a bare bundle) simply has no cold start
//     (see AppleDelegateForwarding.md).
//
// Coexisting with the LocalNotification Apple template: a process has exactly one
// notification-center delegate. Each template installs its OWN delegate and
// retains the one already set as `previous`, forwarding to it — so a delivery
// this delegate does not own (a local notification here) is passed down the
// chain to the delegate that does. Install order does not matter. This template
// therefore relays ONLY remote notifications (UNPushNotificationTrigger); a
// locally scheduled one falls through to `previous`. Source routing is the
// app's job — neither addon judges whether a notification is its own.
//
// The content->JSON mapper below MUST stay in step with the addon's native
// `ApnsNotificationMapper.map(content:)`; if the addon adds a payload field,
// mirror it here or the managed DTO drops it silently.

#import <Foundation/Foundation.h>
#import <dlfcn.h>
#import <objc/runtime.h>
#import <UserNotifications/UserNotifications.h>
#if TARGET_OS_OSX
#import <AppKit/AppKit.h>
#define AXYL_APP_CLASS NSApplication
#else
#import <UIKit/UIKit.h>
#define AXYL_APP_CLASS UIApplication
#endif

typedef void (*AxylApnsTokenCallback)(const char *hexToken);
typedef void (*AxylApnsFailCallback)(const char *errorDescription);
// Relays a delivered notification, serialised to JSON (ApnsNotification shape),
// to managed code. `didReceive` additionally carries the tapped actionIdentifier.
typedef void (*AxylApnsNotificationCallback)(const char *notificationJson);
typedef void (*AxylApnsResponseCallback)(const char *notificationJson, const char *actionIdentifier);
// Relays the RAW launch payload (aps envelope + custom keys) of the notification
// that cold-started the app, as JSON, for NotifyColdStartNotification. Unlike the
// willPresent / didReceive relays (which carry a mapped ApnsNotification), this
// carries the raw userInfo so the addon maps it through map(remotePayload:) — the
// same single source a live cold-start payload would use.
typedef void (*AxylApnsColdStartCallback)(const char *userInfoJson);

static AxylApnsTokenCallback s_tokenCallback = NULL;
static AxylApnsFailCallback s_failCallback = NULL;
static AxylApnsNotificationCallback s_willPresentCallback = NULL;
static AxylApnsResponseCallback s_didReceiveCallback = NULL;
static AxylApnsColdStartCallback s_coldStartCallback = NULL;

// Original delegate IMPs, chained from our replacements when present.
static void (*s_origDidRegister)(id, SEL, AXYL_APP_CLASS *, NSData *) = NULL;
static void (*s_origDidFail)(id, SEL, AXYL_APP_CLASS *, NSError *) = NULL;

static NSString *AxylApnsHexFromToken(NSData *deviceToken) {
    const unsigned char *bytes = (const unsigned char *)deviceToken.bytes;
    NSMutableString *hex = [NSMutableString stringWithCapacity:deviceToken.length * 2];
    for (NSUInteger i = 0; i < deviceToken.length; i++) {
        [hex appendFormat:@"%02x", bytes[i]];
    }
    return hex;
}

static void AxylApnsDidRegister(id self, SEL _cmd, AXYL_APP_CLASS *app, NSData *deviceToken) {
    if (s_origDidRegister) {
        s_origDidRegister(self, _cmd, app, deviceToken);
    }
    if (s_tokenCallback) {
        s_tokenCallback(AxylApnsHexFromToken(deviceToken).UTF8String);
    }
}

static void AxylApnsDidFail(id self, SEL _cmd, AXYL_APP_CLASS *app, NSError *error) {
    if (s_origDidFail) {
        s_origDidFail(self, _cmd, app, error);
    }
    if (s_failCallback) {
        const char *desc = error.localizedDescription.UTF8String;
        s_failCallback(desc ? desc : "remote notification registration failed");
    }
}

// The badge count clamped to Int32 range. Mirrors ApnsNotificationMapper.int32Badge:
// a hand-crafted payload with an out-of-range badge pins to Int32.max rather than
// wrapping to a negative count. `content.badge.intValue` (a 32-bit C int) would wrap,
// so read the full NSInteger and clamp.
static int AxylApnsClampBadge(NSNumber *badge) {
    if (badge == nil) {
        return 0;
    }
    NSInteger raw = badge.integerValue;
    if (raw > INT32_MAX) {
        return INT32_MAX;
    }
    if (raw < INT32_MIN) {
        return INT32_MIN;
    }
    return (int)raw;
}

// Apple's UNNotificationInterruptionLevel (passive=0..critical=3) mapped to the
// generated proto enum, which reserves 0 for Unspecified and so shifts every value
// by one (Passive=1..Critical=4). Mirrors ApnsNotificationMapper.mapInterruptionLevel.
static int AxylApnsInterruptionLevel(UNNotificationInterruptionLevel level) {
    switch (level) {
        case UNNotificationInterruptionLevelPassive: return 1;
        case UNNotificationInterruptionLevelActive: return 2;
        case UNNotificationInterruptionLevelTimeSensitive: return 3;
        case UNNotificationInterruptionLevelCritical: return 4;
        default: return 0;
    }
}

// The full userInfo (nested aps + custom keys) as a compact JSON string, lossless
// for the raw-passthrough contract. Mirrors ApnsNotificationMapper.jsonString:
// normalise to string keys and return "{}" rather than throwing on a non-encodable
// payload, so a malformed notification never breaks the event wire.
static NSString *AxylApnsUserInfoJson(NSDictionary *userInfo) {
    if (userInfo == nil) {
        return @"{}";
    }
    NSMutableDictionary *stringKeyed = [NSMutableDictionary dictionaryWithCapacity:userInfo.count];
    for (id key in userInfo) {
        stringKeyed[[NSString stringWithFormat:@"%@", key]] = userInfo[key];
    }
    if (![NSJSONSerialization isValidJSONObject:stringKeyed]) {
        return @"{}";
    }
    NSData *data = [NSJSONSerialization dataWithJSONObject:stringKeyed options:NSJSONWritingSortedKeys error:NULL];
    NSString *json = data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : nil;
    return json ?: @"{}";
}

// Serialises a delivered UNNotificationContent into the ApnsNotification JSON
// shape the managed side deserialises. Mirrors ApnsNotificationMapper.map(content:)
// field for field: `sound` is intentionally empty (UNNotificationContent exposes
// no public sound-name accessor; the raw aps.sound survives in userInfoJson).
static NSString *AxylApnsNotificationJson(UNNotificationContent *content) {
    NSMutableDictionary *dict = [NSMutableDictionary dictionary];
    dict[@"title"] = content.title ?: @"";
    dict[@"subtitle"] = content.subtitle ?: @"";
    dict[@"body"] = content.body ?: @"";
    dict[@"badge"] = @(AxylApnsClampBadge(content.badge));
    dict[@"sound"] = @"";
    dict[@"categoryIdentifier"] = content.categoryIdentifier ?: @"";
    dict[@"threadIdentifier"] = content.threadIdentifier ?: @"";
#if TARGET_OS_OSX
    dict[@"launchImageName"] = @""; // iOS / Mac Catalyst only
#else
    dict[@"launchImageName"] = content.launchImageName ?: @"";
#endif
    dict[@"targetContentIdentifier"] = content.targetContentIdentifier ?: @"";
    dict[@"interruptionLevel"] = @(AxylApnsInterruptionLevel(content.interruptionLevel));
    dict[@"relevanceScore"] = @(content.relevanceScore);
    dict[@"userInfoJson"] = AxylApnsUserInfoJson(content.userInfo);

    if (![NSJSONSerialization isValidJSONObject:dict]) {
        return @"{}";
    }
    NSData *data = [NSJSONSerialization dataWithJSONObject:dict options:0 error:NULL];
    NSString *json = data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : nil;
    return json ?: @"{}";
}

// APNs delivers with a UNPushNotificationTrigger; a locally scheduled one carries
// a time-interval / calendar trigger, or none at all — `isKindOfClass:` answers NO
// for a nil trigger. Only remote notifications are this template's to relay.
static BOOL AxylApnsIsRemote(UNNotification *notification) {
    return [notification.request.trigger isKindOfClass:[UNPushNotificationTrigger class]];
}

// UNUserNotificationCenterDelegate: relays a remote delivery and supplies the
// foreground presentation policy. A notification it does not own, and any selector
// it does not terminate, is forwarded to `previous` so a chained delegate (e.g. the
// LocalNotification template, or the game's own) still runs.
@interface AxylApnsCenterDelegate : NSObject <UNUserNotificationCenterDelegate>
@property (nonatomic, strong) id<UNUserNotificationCenterDelegate> previous;
@end

// The app's foreground display policy for a remote delivery (addon contract), and the
// fallback for anything nobody else owns. Edit here to change what a foreground push
// shows. `.alert` is deprecated since iOS 14; `.banner` + `.list` replace it.
static const UNNotificationPresentationOptions kAxylApnsForegroundOptions =
    UNNotificationPresentationOptionBanner
    | UNNotificationPresentationOptionList
    | UNNotificationPresentationOptionSound
    | UNNotificationPresentationOptionBadge;

@implementation AxylApnsCenterDelegate

- (void)userNotificationCenter:(UNUserNotificationCenter *)center
    willPresentNotification:(UNNotification *)notification
    withCompletionHandler:(void (^)(UNNotificationPresentationOptions))completionHandler {
    // A remote delivery is ours: relay it to the addon, then answer the OS here. It is not
    // forwarded down the chain: a delegate behind us owns other notifications, not this
    // one, and may answer for it wrongly — Unity Mobile Notifications, with remote
    // notifications off in its settings, completes a push it is handed with None and the
    // banner never shows.
    //
    // LocalNotificationAppleForwarder still relays and then defers, which is the shape this
    // replaces. It does not need the same change: it is macOS-only and Unity Mobile
    // Notifications does not build for macOS, so the two never share a process
    // (AppleDelegateForwarding.md, "Using both add-ons").
    if (AxylApnsIsRemote(notification)) {
        if (s_willPresentCallback) {
            s_willPresentCallback(AxylApnsNotificationJson(notification.request.content).UTF8String);
        }
        completionHandler(kAxylApnsForegroundOptions);
        return;
    }
    // Not ours (a local notification): the delegate that owns it decides.
    if ([self.previous respondsToSelector:_cmd]) {
        [self.previous userNotificationCenter:center
            willPresentNotification:notification
            withCompletionHandler:completionHandler];
        return;
    }
    // End of chain with no owner: show it.
    completionHandler(kAxylApnsForegroundOptions);
}

- (void)userNotificationCenter:(UNUserNotificationCenter *)center
    didReceiveNotificationResponse:(UNNotificationResponse *)response
    withCompletionHandler:(void (^)(void))completionHandler {
    if (AxylApnsIsRemote(response.notification)) {
        UNNotificationContent *content = response.notification.request.content;
        if (s_didReceiveCallback) {
            s_didReceiveCallback(
                AxylApnsNotificationJson(content).UTF8String,
                response.actionIdentifier.UTF8String);
        }
    }
    // Late launch tap: if Install's take ran before the launch didReceive landed, the
    // capturer seeds its buffer only now — pick it up so the payload is not stranded. The
    // capturer's take is one-shot and only a launch tap seeds it, so this never double-relays
    // and a warm tap finds nothing. Dispatched so it runs after the chained capturer below
    // has captured (the chain call is synchronous within this delivery).
    if (s_coldStartCallback) {
        AxylApnsColdStartCallback coldStartCallback = s_coldStartCallback;
        dispatch_async(dispatch_get_main_queue(), ^{
            const char *(*take)(void) =
                (const char *(*)(void))dlsym(RTLD_DEFAULT, "AxylApnsColdStartTake");
            const char *lateJson = (take != NULL) ? take() : NULL;
            if (lateJson != NULL) {
                if (lateJson[0] != '\0') {
                    coldStartCallback(lateJson);
                }
                free((void *)lateJson);
            }
        });
    }
    if ([self.previous respondsToSelector:_cmd]) {
        [self.previous userNotificationCenter:center
            didReceiveNotificationResponse:response
            withCompletionHandler:completionHandler];
        return;
    }
    completionHandler();
}

@end

// Strong ref: UNUserNotificationCenter.delegate is weak, so without this the
// delegate would deallocate immediately after assignment.
static AxylApnsCenterDelegate *s_centerDelegate = nil;

// The app-delegate swizzle must run exactly once. A second install would let
// class_getInstanceMethod return our own AxylApnsDidRegister and store it back
// into s_origDidRegister, so a later token delivery would call itself forever.
static BOOL s_appDelegateSwizzled = NO;

// Whether our delegate is the center's, or sits somewhere down the chain a later
// template built on top of it (each template chains its `previous`). Re-pointing the
// center at ourselves in that case would put the template above us into our own
// `previous` and the two would forward to each other forever.
static BOOL AxylApnsDelegateIsChained(id delegate) {
    // 8 is a depth no real layout reaches — a chain is one template per SDK, two or three at
    // most. The bound only keeps a malformed cycle (two templates already pointing at each
    // other) from walking forever; a chain that deep reads as not ours and gets re-chained.
    id cursor = delegate;
    for (int hops = 0; cursor != nil && hops < 8; hops++) {
        if (cursor == s_centerDelegate) {
            return YES;
        }
        cursor = [cursor respondsToSelector:@selector(previous)]
            ? [cursor valueForKey:@"previous"]
            : nil;
    }
    return NO;
}

static void AxylApnsInstallCenterDelegate() {
    UNUserNotificationCenter *center = [UNUserNotificationCenter currentNotificationCenter];
    if (s_centerDelegate == nil) {
        s_centerDelegate = [[AxylApnsCenterDelegate alloc] init];
    } else if (AxylApnsDelegateIsChained(center.delegate)) {
        return; // still wired — nothing replaced it since
    }
    // Chain whatever is set now. On the first install that is what the app or an engine
    // package set at launch; on a re-install it is whatever replaced us since — Unity
    // Mobile Notifications, for one, points the center at its own delegate on every
    // AuthorizationRequest without chaining, which is why the app re-runs Install after
    // a permission ask.
    s_centerDelegate.previous = center.delegate;
    center.delegate = s_centerDelegate;
}

extern "C" void AxylApnsForwarderInstall(AxylApnsTokenCallback tokenCb,
    AxylApnsFailCallback failCb,
    AxylApnsNotificationCallback willPresentCb,
    AxylApnsResponseCallback didReceiveCb,
    AxylApnsColdStartCallback coldStartCb) {
    s_tokenCallback = tokenCb;
    s_failCallback = failCb;
    // Set before installing the delegate so an early notification finds them; the
    // delegate reads these file-statics at call time, so a re-install with a new
    // plugin re-points the relay. The delegate itself is touched only when something
    // replaced it since (see AxylApnsInstallCenterDelegate).
    s_willPresentCallback = willPresentCb;
    s_didReceiveCallback = didReceiveCb;
    s_coldStartCallback = coldStartCb;

    // Relay the cold-start payload captured by the companion ApnsColdStartCapture.mm.
    // Resolved via dlsym(RTLD_DEFAULT) rather than a direct call so that when this forwarder
    // runs as a macOS bundle it still reaches the capturer compiled INTO the app across the
    // link boundary (RTLD_DEFAULT's search reaches the main executable).
    //
    // The callback fires exactly once per Install, with the payload OR with "" when this
    // launch carried none (normal launch, or no capturer in the build) — the empty relay is
    // what lets the managed side complete its cold-start readiness signal, so a game can
    // await "the cold-start state is known" instead of guessing at ordering.
    //
    // Relayed on the NEXT main-loop turn, not synchronously: Install is itself a
    // managed->native call, and the callback re-enters managed to start an async RPC
    // (managed->native again). Firing it synchronously runs that RPC on the re-entrant stack,
    // where it does not complete; dispatching to the main queue lets it run on a clean stack.
    if (s_coldStartCallback) {
        const char *(*take)(void) = (const char *(*)(void))dlsym(RTLD_DEFAULT, "AxylApnsColdStartTake");
        const char *coldStartJson = (take != NULL) ? take() : NULL;
        const char *relayed = coldStartJson ?: strdup("");
        AxylApnsColdStartCallback coldStartCallback = s_coldStartCallback;
        dispatch_async(dispatch_get_main_queue(), ^{
            coldStartCallback(relayed);
            free((void *)relayed);
        });
    }

    AxylApnsInstallCenterDelegate();

    // A re-install is fully handled above (relay callbacks, center delegate); the swizzle
    // below must not run twice (see s_appDelegateSwizzled).
    if (s_appDelegateSwizzled) {
        return;
    }

    id delegate = [AXYL_APP_CLASS sharedApplication].delegate;
    if (delegate == nil) {
        // Leave s_appDelegateSwizzled NO so a later install (once the delegate
        // exists) still swizzles.
        NSLog(@"[AxylApns] no app delegate at install time; registration callbacks not forwarded");
        return;
    }
    Class cls = [delegate class];
    s_appDelegateSwizzled = YES;

    SEL registerSel = @selector(application:didRegisterForRemoteNotificationsWithDeviceToken:);
    Method registerMethod = class_getInstanceMethod(cls, registerSel);
    if (registerMethod) {
        s_origDidRegister = (void (*)(id, SEL, AXYL_APP_CLASS *, NSData *))
            method_setImplementation(registerMethod, (IMP)AxylApnsDidRegister);
    } else {
        class_addMethod(cls, registerSel, (IMP)AxylApnsDidRegister, "v@:@@");
    }

    SEL failSel = @selector(application:didFailToRegisterForRemoteNotificationsWithError:);
    Method failMethod = class_getInstanceMethod(cls, failSel);
    if (failMethod) {
        s_origDidFail = (void (*)(id, SEL, AXYL_APP_CLASS *, NSError *))
            method_setImplementation(failMethod, (IMP)AxylApnsDidFail);
    } else {
        class_addMethod(cls, failSel, (IMP)AxylApnsDidFail, "v@:@@");
    }
}

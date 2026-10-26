// Copyright (c) Com2uS Platform Corp. All rights reserved.
//
// COLD-START CAPTURER — companion to ApnsRemoteNotificationForwarder.
//
// A remote-notification tap that launches a TERMINATED app is delivered to the
// UNUserNotificationCenterDelegate's didReceive as the app finishes launching — long before
// the app calls the forwarder's Install() at game startup. So the forwarder's own delegate is
// too late to catch that launch tap. This companion installs an EARLY, minimal delegate at
// didFinishLaunching (its +load registers the launch observer before launch completes) that
// grabs the launch tap's payload once, holds it, and hands it to Install through the
// AxylApnsColdStartTake export (resolved by dlsym). Install runs after the launch tap has
// already been captured, drains it, then installs the forwarder delegate chained in front.
//
// Platform difference in WHERE the payload comes from:
//   * iOS   — the raw launch payload is in launchOptions[.remoteNotification] at launch, so
//             this just reads it; no delegate needed.
//   * macOS — the launch key (NSApplicationLaunchUserNotificationKey) response carries an
//             EMPTY content.userInfo, so the payload must come from the delegate's didReceive.
//             The launch key's PRESENCE is used only to gate "this launch was a notification
//             tap", so a later warm tap in a normally-launched session is never mistaken for a
//             cold start.
//
// Why a SEPARATE file from the forwarder: it must load before launch (source-compiled INTO the
// macOS app; the forwarder ships as a late-loading bundle for Notify*), and its Obj-C classes
// share no name with the forwarder's, so the app copy and the bundle never register the same
// class twice. It carries its own tiny remote-check / userInfo->JSON helpers to stay
// self-contained.

#import <Foundation/Foundation.h>
#import <os/lock.h>
#import <UserNotifications/UserNotifications.h>
#if TARGET_OS_OSX
#import <AppKit/AppKit.h>
#else
#import <UIKit/UIKit.h>
#endif

// The launch payload captured for the forwarder's Install to drain. Populated ONLY when a
// notification launched the app; a normal launch leaves it nil so nothing is seeded.
static NSString *s_pendingColdStartJson = nil;

// Guards s_pendingColdStartJson (and the macOS launch flag): the observers write on the main
// thread, but AxylApnsColdStartTake is driven by the game's Install call, which carries no
// thread contract — so every read/write/clear takes this lock.
static os_unfair_lock s_coldStartLock = OS_UNFAIR_LOCK_INIT;

// Only a remote delivery is the APNs template's to relay; a locally scheduled one carries a
// different (or nil) trigger and falls to the LocalNotification template.
static BOOL AxylApnsCaptureIsRemote(UNNotification *notification) {
    return [notification.request.trigger isKindOfClass:[UNPushNotificationTrigger class]];
}

// The full userInfo (nested aps + custom keys) as a compact JSON string, lossless for the
// raw-passthrough contract. Mirrors ApnsRemoteNotificationForwarder's AxylApnsUserInfoJson:
// normalise to string keys and return "{}" rather than throwing on a non-encodable payload.
static NSString *AxylApnsCaptureUserInfoJson(NSDictionary *userInfo) {
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

#if TARGET_OS_OSX
// True only between launch and the first tap, and only when the app was launched by a
// notification — so the early delegate below seeds cold start from the launch tap alone.
static BOOL s_launchedByNotification = NO;

// A minimal early delegate. It captures the launch tap's payload once (gated by the launch
// flag) and forwards didReceive to whatever delegate was set before it (usually none at
// launch). Once the app calls Install, the forwarder chains its own delegate in front of this
// one, so subsequent taps flow forwarder -> here -> previous. It deliberately implements ONLY
// didReceive. Before Install it is usually the frontmost delegate — the LocalNotification
// template may already sit ahead of it — and a willPresent here would decide foreground
// display for whatever reaches it in that window, where the no-delegate baseline is the
// right behavior. After Install the forwarder answers remote deliveries itself and forwards
// only what it does not own, so a willPresent here would see local ones alone.
@interface AxylApnsColdStartCaptureDelegate : NSObject <UNUserNotificationCenterDelegate>
@property (nonatomic, strong) id<UNUserNotificationCenterDelegate> previous;
@end

@implementation AxylApnsColdStartCaptureDelegate

- (void)userNotificationCenter:(UNUserNotificationCenter *)center
    didReceiveNotificationResponse:(UNNotificationResponse *)response
    withCompletionHandler:(void (^)(void))completionHandler {
    os_unfair_lock_lock(&s_coldStartLock);
    if (s_launchedByNotification && s_pendingColdStartJson == nil
        && AxylApnsCaptureIsRemote(response.notification)) {
        s_pendingColdStartJson =
            AxylApnsCaptureUserInfoJson(response.notification.request.content.userInfo);
    }
    // Only the launch tap seeds cold start; clear so a later warm tap never does.
    s_launchedByNotification = NO;
    os_unfair_lock_unlock(&s_coldStartLock);
    if ([self.previous respondsToSelector:_cmd]) {
        [self.previous userNotificationCenter:center
            didReceiveNotificationResponse:response
            withCompletionHandler:completionHandler];
        return;
    }
    completionHandler();
}

@end

// Strong ref: UNUserNotificationCenter.delegate is weak.
static AxylApnsColdStartCaptureDelegate *s_captureDelegate = nil;
#endif

@interface AxylApnsColdStartObserver : NSObject
@end
@implementation AxylApnsColdStartObserver
+ (void)load {
#if TARGET_OS_OSX
    // Install the capture delegate as EARLY as possible (willFinishLaunching) so it is in place
    // before macOS delivers the launch tap's didReceive; capture is still gated on the launch
    // flag set below, so this early install is harmless on a normal launch.
    [[NSNotificationCenter defaultCenter]
        addObserverForName:NSApplicationWillFinishLaunchingNotification
        object:nil
        queue:nil
        usingBlock:^(NSNotification *note) {
            UNUserNotificationCenter *center = [UNUserNotificationCenter currentNotificationCenter];
            AxylApnsColdStartCaptureDelegate *delegate = [[AxylApnsColdStartCaptureDelegate alloc] init];
            delegate.previous = center.delegate; // chain whatever was already set (usually nil)
            s_captureDelegate = delegate;
            center.delegate = delegate;
        }];
    // The launch key rides on didFinishLaunching. Its response has an empty content.userInfo on
    // macOS, so use its PRESENCE only to gate cold start — the payload comes from the delegate's
    // didReceive (delivered just after this, once launch finishes).
    [[NSNotificationCenter defaultCenter]
        addObserverForName:NSApplicationDidFinishLaunchingNotification
        object:nil
        queue:nil
        usingBlock:^(NSNotification *note) {
            if (note.userInfo[NSApplicationLaunchUserNotificationKey] != nil) {
                os_unfair_lock_lock(&s_coldStartLock);
                s_launchedByNotification = YES;
                os_unfair_lock_unlock(&s_coldStartLock);
            }
        }];
#else
    [[NSNotificationCenter defaultCenter]
        addObserverForName:UIApplicationDidFinishLaunchingNotification
        object:nil
        queue:nil
        usingBlock:^(NSNotification *note) {
            // UIApplicationLaunchOptionsRemoteNotificationKey is deprecated on iOS 26, but it is
            // still the way to read the payload that cold-LAUNCHED the app. Intentional use —
            // silence the deprecation so a warnings-as-error consumer project still builds. A
            // normal launch has no such key, so s_pendingColdStartJson stays nil.
            #pragma clang diagnostic push
            #pragma clang diagnostic ignored "-Wdeprecated-declarations"
            NSDictionary *remote = note.userInfo[UIApplicationLaunchOptionsRemoteNotificationKey];
            #pragma clang diagnostic pop
            if ([remote isKindOfClass:[NSDictionary class]]) {
                os_unfair_lock_lock(&s_coldStartLock);
                s_pendingColdStartJson = AxylApnsCaptureUserInfoJson(remote);
                os_unfair_lock_unlock(&s_coldStartLock);
            }
        }];
#endif
}
@end

// Hands the captured cold-start payload to the forwarder's Install, ACROSS a possible link
// boundary (macOS: this file compiled into the app, the forwarder in a bundle). Install
// resolves it by name with dlsym(RTLD_DEFAULT). One-shot: cleared after taking so a re-install
// cannot seed it twice. Returns a strdup'd C string the caller must free, or NULL when nothing
// launched the app. visibility("default") keeps it dlsym-findable under -fvisibility=hidden.
// `used` keeps the symbol alive under -dead_strip: the only reference to it is the dlsym
// string in the forwarder, which is not a linker reference root ("visibility" alone only
// defends against -fvisibility=hidden).
__attribute__((visibility("default"), used))
extern "C" const char *AxylApnsColdStartTake(void) {
    os_unfair_lock_lock(&s_coldStartLock);
    NSString *pending = s_pendingColdStartJson;
    s_pendingColdStartJson = nil;
    os_unfair_lock_unlock(&s_coldStartLock);
    if (pending == nil) {
        return NULL;
    }
    return strdup(pending.UTF8String);
}

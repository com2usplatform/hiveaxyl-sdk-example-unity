// Copyright (c) Com2uS Platform Corp. All rights reserved.
//
// STANDARD DELEGATE TEMPLATE — LocalNotification Apple (local push) only.
//
// The LocalNotification Apple addon installs no delegate by design: the app owns its
// `UNUserNotificationCenterDelegate` and forwards the delegate callbacks into the
// addon's game-driven `Notify*` RPCs. Unity owns the app and installs no such
// delegate, so this file installs one for you. The addon is macOS-only — iOS local
// notifications belong to Unity's Mobile Notifications package — so copy this into
// your project's macOS native plugins folder, where the standalone player loads it as
// a prebuilt bundle (see AppleDelegateForwarding.md), and wire
// LocalNotificationAppleForwarder.cs.
//
// Unlike the APNs template there is no app-delegate swizzling: locally scheduled
// notifications arrive only through the notification-center delegate, never through
// the remote-registration selectors.
//
// Coexisting with the APNs template: a process has exactly one notification-center
// delegate. Each template installs its OWN delegate and retains the one already set
// as `previous`, forwarding to it — so a delivery this delegate does not own (a
// remote notification here) is passed down the chain to the delegate that does.
// Install order does not matter. This template therefore relays ONLY locally
// scheduled notifications; a remote one (UNPushNotificationTrigger) falls through to
// `previous`. Source routing is the app's job — neither addon judges whether a
// notification is its own.
//
// The content->JSON mapper below MUST stay in step with the addon's native
// `LocalNotificationContentMapper`; if the addon adds a payload field, mirror it
// here or the managed DTO drops it silently.

#import <Foundation/Foundation.h>
#import <UserNotifications/UserNotifications.h>

// Carries a NotificationPayload-shaped JSON (notificationId + content). `didReceive`
// additionally carries the tapped actionIdentifier.
typedef void (*AxylLocalNotificationCallback)(const char *payloadJson);
typedef void (*AxylLocalResponseCallback)(const char *payloadJson, const char *actionIdentifier);

static AxylLocalNotificationCallback s_willPresentCallback = NULL;
static AxylLocalResponseCallback s_didReceiveCallback = NULL;

// The badge count clamped to Int32 range. Mirrors LocalNotificationContentMapper:
// an out-of-range badge pins to Int32.max rather than wrapping to a negative count.
static int AxylLocalClampBadge(NSNumber *badge) {
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
// by one (Passive=1..Critical=4). Mirrors LocalNotificationContentMapper.
static int AxylLocalInterruptionLevel(UNNotificationInterruptionLevel level) {
    switch (level) {
        case UNNotificationInterruptionLevelPassive: return 1;
        case UNNotificationInterruptionLevelActive: return 2;
        case UNNotificationInterruptionLevelTimeSensitive: return 3;
        case UNNotificationInterruptionLevelCritical: return 4;
        default: return 0;
    }
}

// The full userInfo as a compact JSON string, lossless for the raw-passthrough
// contract. Normalise to string keys and return "{}" rather than throwing on a
// non-encodable payload, so a malformed notification never breaks the event wire.
static NSString *AxylLocalUserInfoJson(NSDictionary *userInfo) {
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

// Serialises a delivered request into the addon's NotificationPayload JSON shape
// (notificationId + content). Mirrors LocalNotificationContentMapper: the content
// fields are the NotificationContent subset, which — unlike the APNs ApnsNotification
// — carries no launchImageName or targetContentIdentifier, and adds `attachments`
// (empty here: a delivered notification's attachments are OS-owned local files not
// relayed). `sound` is empty — UNNotificationContent exposes no public sound-name
// accessor.
static NSString *AxylLocalPayloadJson(UNNotificationRequest *request) {
    UNNotificationContent *content = request.content;
    NSMutableDictionary *contentDict = [NSMutableDictionary dictionary];
    contentDict[@"title"] = content.title ?: @"";
    contentDict[@"subtitle"] = content.subtitle ?: @"";
    contentDict[@"body"] = content.body ?: @"";
    contentDict[@"badge"] = @(AxylLocalClampBadge(content.badge));
    contentDict[@"sound"] = @"";
    contentDict[@"categoryIdentifier"] = content.categoryIdentifier ?: @"";
    contentDict[@"threadIdentifier"] = content.threadIdentifier ?: @"";
    contentDict[@"interruptionLevel"] = @(AxylLocalInterruptionLevel(content.interruptionLevel));
    contentDict[@"relevanceScore"] = @(content.relevanceScore);
    contentDict[@"userInfoJson"] = AxylLocalUserInfoJson(content.userInfo);
    contentDict[@"attachments"] = @[];

    NSMutableDictionary *dict = [NSMutableDictionary dictionary];
    dict[@"notificationId"] = request.identifier ?: @"";
    dict[@"content"] = contentDict;

    if (![NSJSONSerialization isValidJSONObject:dict]) {
        return @"{}";
    }
    NSData *data = [NSJSONSerialization dataWithJSONObject:dict options:0 error:NULL];
    NSString *json = data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : nil;
    return json ?: @"{}";
}

// A locally scheduled notification carries a time-interval / calendar trigger, or
// none at all; a remote one carries a UNPushNotificationTrigger. Only non-remote
// deliveries are this template's to relay.
static BOOL AxylLocalIsRemote(UNNotification *notification) {
    return [notification.request.trigger isKindOfClass:[UNPushNotificationTrigger class]];
}

// UNUserNotificationCenterDelegate: relays a local delivery and supplies the
// foreground presentation policy. A notification it does not own, and any selector
// it does not terminate, is forwarded to `previous` so a chained delegate (e.g. the
// APNs template, or the game's own) still runs.
@interface AxylLocalCenterDelegate : NSObject <UNUserNotificationCenterDelegate>
@property (nonatomic, strong) id<UNUserNotificationCenterDelegate> previous;
@end

@implementation AxylLocalCenterDelegate

- (void)userNotificationCenter:(UNUserNotificationCenter *)center
    willPresentNotification:(UNNotification *)notification
    withCompletionHandler:(void (^)(UNNotificationPresentationOptions))completionHandler {
    // Relay before chaining. Only local deliveries are ours; a remote one falls
    // through to `previous` (the APNs template or the game's delegate).
    if (!AxylLocalIsRemote(notification) && s_willPresentCallback) {
        s_willPresentCallback(AxylLocalPayloadJson(notification.request).UTF8String);
    }
    if ([self.previous respondsToSelector:_cmd]) {
        [self.previous userNotificationCenter:center
            willPresentNotification:notification
            withCompletionHandler:completionHandler];
        return;
    }
    // End of chain: the app's foreground display decision (addon contract).
    // `.alert` is deprecated since iOS 14; `.banner` + `.list` replace it.
    completionHandler(UNNotificationPresentationOptionBanner
                    | UNNotificationPresentationOptionList
                    | UNNotificationPresentationOptionSound
                    | UNNotificationPresentationOptionBadge);
}

- (void)userNotificationCenter:(UNUserNotificationCenter *)center
    didReceiveNotificationResponse:(UNNotificationResponse *)response
    withCompletionHandler:(void (^)(void))completionHandler {
    if (!AxylLocalIsRemote(response.notification) && s_didReceiveCallback) {
        const char *actionIdentifier = response.actionIdentifier.UTF8String;
        s_didReceiveCallback(
            AxylLocalPayloadJson(response.notification.request).UTF8String,
            actionIdentifier);
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
static AxylLocalCenterDelegate *s_centerDelegate = nil;

extern "C" void AxylLocalNotificationForwarderInstall(AxylLocalNotificationCallback willPresentCb,
    AxylLocalResponseCallback didReceiveCb) {
    s_willPresentCallback = willPresentCb;
    s_didReceiveCallback = didReceiveCb;

    if (s_centerDelegate != nil) {
        return; // idempotent per process
    }
    UNUserNotificationCenter *center = [UNUserNotificationCenter currentNotificationCenter];
    AxylLocalCenterDelegate *delegate = [[AxylLocalCenterDelegate alloc] init];
    delegate.previous = center.delegate; // chain whatever was already set
    s_centerDelegate = delegate;
    center.delegate = delegate;
}

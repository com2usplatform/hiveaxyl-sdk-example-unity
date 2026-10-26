// Copyright (c) Com2uS Platform Corp. All rights reserved.

// The standard delegate forwarder. This Recipe folder is where it lives: copy the folder into
// your project as it is, native source included, and change the namespace to your own.
namespace Hive.Axyl.Samples.Recipes
{
    using Hive.Axyl.Push.Addon.AppleNotification;

    /// <summary>
    /// Managed side of the LocalNotification Apple delegate forwarding. Receives the
    /// native callbacks installed by <c>LocalNotificationAppleForwarder.mm</c> and forwards
    /// a locally-scheduled notification's foreground presentation / tap into
    /// <see cref="ILocalNotificationApplePlugin"/>'s <c>Notify*</c> RPCs, so
    /// <c>NotificationPresented</c> / <c>NotificationOpened</c> fire.
    ///
    /// Only locally-scheduled deliveries reach this class: the native delegate relays
    /// a local notification and chains any other to the delegate already installed
    /// (e.g. the APNs template). Source routing is the app's job — neither addon
    /// judges whether a notification is its own.
    /// </summary>
    public static class LocalNotificationAppleForwarder
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_OSX
        // Kept in static fields so the GC cannot collect the delegates while the
        // native side holds their function pointers.
        private static readonly NotificationCallback s_onWillPresent = OnWillPresent;
        private static readonly ResponseCallback s_onDidReceiveResponse = OnDidReceiveResponse;
        private static ILocalNotificationApplePlugin s_plugin;

        private delegate void NotificationCallback([System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.LPStr)] string payloadJson);

        private delegate void ResponseCallback(
            [System.Runtime.InteropServices.MarshalAs(
                System.Runtime.InteropServices.UnmanagedType.LPStr)] string payloadJson,
            [System.Runtime.InteropServices.MarshalAs(
                System.Runtime.InteropServices.UnmanagedType.LPStr)] string actionIdentifier);

        // macOS standalone loads the prebuilt bundle by name. The name must match
        // the bundle built from LocalNotificationAppleForwarder.mm.
        private const string k_NativeLib = "LocalNotificationAppleForwarder";

        [System.Runtime.InteropServices.DllImport(k_NativeLib)]
        private static extern void AxylLocalNotificationForwarderInstall(
            NotificationCallback onWillPresent,
            ResponseCallback onDidReceiveResponse);

        /// <summary>
        /// Installs the native delegate forwarding for <paramref name="plugin"/>.
        /// Idempotent per process; the last-installed plugin receives callbacks.
        /// </summary>
        /// <param name="plugin">The addon instance the OS callbacks are forwarded to.</param>
        public static void Install(ILocalNotificationApplePlugin plugin)
        {
            s_plugin = plugin;
            AxylLocalNotificationForwarderInstall(s_onWillPresent, s_onDidReceiveResponse);
        }

        [AOT.MonoPInvokeCallback(typeof(NotificationCallback))]
        private static void OnWillPresent(string payloadJson)
        {
            var plugin = s_plugin;
            if (plugin == null)
            {
                return;
            }

            // Fire-and-forget: raises NotificationPresented on the foreground path.
            _ = plugin.NotifyWillPresentNotificationAsync(ParsePayload(payloadJson));
        }

        [AOT.MonoPInvokeCallback(typeof(ResponseCallback))]
        private static void OnDidReceiveResponse(string payloadJson, string actionIdentifier)
        {
            var plugin = s_plugin;
            if (plugin == null)
            {
                return;
            }

            // Raises NotificationOpened on the tap path; the actionIdentifier
            // rides along (default tap or a custom action).
            _ = plugin.NotifyDidReceiveResponseAsync(
                ParsePayload(payloadJson), actionIdentifier ?? string.Empty);
        }

        // Deserialises the native JSON (NotificationPayload shape, built by
        // AxylLocalPayloadJson to mirror the addon's own serializer) into the DTO with
        // the SDK's generated FromJsonString, so the template needs no external JSON
        // library. A malformed payload maps to an empty one rather than throwing, so a
        // native callback never crashes the app — and never trips the RPC's
        // ArgumentNullException on a null notification.
        private static NotificationPayload ParsePayload(string payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson))
            {
                return new NotificationPayload();
            }

            try
            {
                return NotificationPayload.FromJsonString(payloadJson);
            }
            catch (Hive.Axyl.Core.Serialization.JsonParseException)
            {
                return new NotificationPayload();
            }
        }
#else
        /// <summary>No-op off Apple players (Editor / other platforms).</summary>
        public static void Install(ILocalNotificationApplePlugin plugin)
        {
        }
#endif
    }
}

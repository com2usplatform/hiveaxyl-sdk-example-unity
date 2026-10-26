// Copyright (c) Com2uS Platform Corp. All rights reserved.

// The standard delegate forwarder. This Recipe folder is where it lives: copy the folder into
// your project as it is, native source included, and change the namespace to your own.
namespace Hive.Axyl.Samples.Recipes
{
    using Hive.Axyl.Push.Addon.APNS;

    /// <summary>
    /// Managed side of the APNs delegate forwarding. Receives the native callbacks
    /// installed by <c>ApnsRemoteNotificationForwarder.mm</c> and forwards them into
    /// <see cref="IAPNSPlugin"/>'s game-driven <c>Notify*</c> RPCs. Without this,
    /// the device token the OS delivers never reaches the addon, so
    /// <c>GetTokenAsync</c> stays pending and <c>TokenRefreshed</c> never fires
    /// (the addon is stateless and installs no app delegate by design).
    ///
    /// All five delegate paths are wired by <see cref="Install"/>: the token
    /// path (didRegister / didFailToRegister), and — via the native
    /// <c>UNUserNotificationCenterDelegate</c> the .mm installs — willPresent /
    /// didReceiveResponse, which relay a JSON-serialised notification into
    /// <c>NotifyWillPresentNotificationAsync</c> / <c>NotifyDidReceiveResponseAsync</c>
    /// so <c>NotificationPresented</c> / <c>NotificationOpened</c> fire, plus the
    /// cold-start relay — the launch tap's raw payload goes to
    /// <c>NotifyColdStartNotificationAsync</c> (captured by the companion
    /// <c>ApnsColdStartCapture.mm</c>: iOS reads <c>launchOptions</c>; macOS gates on the
    /// launch key and takes the payload from the launch tap's early didReceive), seeding
    /// the buffer that <c>GetColdStartNotificationAsync</c> drains. Cold start needs the
    /// capturer loaded before launch — automatic on iOS, but on macOS it must be
    /// compiled into the app (see AppleDelegateForwarding.md).
    ///
    /// Only remote deliveries reach this class: the native delegate relays a remote
    /// notification and chains any other to the delegate already installed (e.g. the
    /// LocalNotification template). Source routing is the app's job — neither addon
    /// judges whether a notification is its own.
    /// </summary>
    public static class ApnsRemoteNotificationForwarder
    {
#if !UNITY_EDITOR && (UNITY_STANDALONE_OSX || UNITY_IOS)
        // Kept in static fields so the GC cannot collect the delegates while the
        // native side holds their function pointers.
        private static readonly TokenCallback s_onToken = OnToken;
        private static readonly FailCallback s_onFail = OnFail;
        private static readonly NotificationCallback s_onWillPresent = OnWillPresent;
        private static readonly ResponseCallback s_onDidReceiveResponse = OnDidReceiveResponse;
        private static readonly ColdStartCallback s_onColdStart = OnColdStart;
        private static IAPNSPlugin s_plugin;

        // Completed once this launch's cold-start state is KNOWN: after the relay RPC finished
        // seeding the addon's buffer (launch tap), or as soon as the native side reports the
        // launch carried none (empty relay). Install always triggers exactly one of the two.
        private static readonly System.Threading.Tasks.TaskCompletionSource<bool> s_coldStartRelay =
            new System.Threading.Tasks.TaskCompletionSource<bool>(
                System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Completes when the launch's cold-start state is known — the relay into
        /// <see cref="IAPNSPlugin.NotifyColdStartNotificationAsync"/> has finished (or this
        /// launch carried no notification). Await this before the startup
        /// <c>GetColdStartNotificationAsync</c>; querying earlier can race the relay and
        /// drain the single-use buffer while it is still empty.
        /// </summary>
        public static System.Threading.Tasks.Task ColdStartRelayed => s_coldStartRelay.Task;

        private delegate void TokenCallback([System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.LPStr)] string hexToken);

        private delegate void FailCallback([System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.LPStr)] string errorDescription);

        private delegate void NotificationCallback([System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.LPStr)] string notificationJson);

        private delegate void ResponseCallback(
            [System.Runtime.InteropServices.MarshalAs(
                System.Runtime.InteropServices.UnmanagedType.LPStr)] string notificationJson,
            [System.Runtime.InteropServices.MarshalAs(
                System.Runtime.InteropServices.UnmanagedType.LPStr)] string actionIdentifier);

        private delegate void ColdStartCallback([System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.LPStr)] string userInfoJson);

        // iOS static-links the .mm (__Internal). macOS standalone loads it as a prebuilt
        // bundle named to match this constant; that bundle serves the Notify* path on every
        // macOS build (including a bare .app). Cold start additionally needs the .mm compiled
        // INTO the Xcode-export app (its +load must run before launch); that compiled-in copy
        // coexists with the bundle and hands its captured payload to the bundle's Install via
        // the AxylApnsColdStartTake export — so k_NativeLib stays the bundle name (see AppleDelegateForwarding.md).
#if UNITY_IOS
        private const string k_NativeLib = "__Internal";
#else
        private const string k_NativeLib = "ApnsRemoteNotificationForwarder";
#endif

        [System.Runtime.InteropServices.DllImport(k_NativeLib)]
        private static extern void AxylApnsForwarderInstall(
            TokenCallback onToken,
            FailCallback onFail,
            NotificationCallback onWillPresent,
            ResponseCallback onDidReceiveResponse,
            ColdStartCallback onColdStart);

        /// <summary>
        /// Installs the native delegate forwarding for <paramref name="plugin"/>.
        /// Safe to call repeatedly: a no-op while the template's delegate is still in the
        /// notification-center chain, and re-chains it when something replaced
        /// <c>UNUserNotificationCenter.delegate</c> without chaining. The last-installed
        /// plugin receives callbacks. Call after resolving <see cref="IAPNSPlugin"/> and
        /// before <c>GetTokenAsync</c>.
        /// </summary>
        /// <param name="plugin">The addon instance the OS callbacks are forwarded to.</param>
        public static void Install(IAPNSPlugin plugin)
        {
            s_plugin = plugin;
            AxylApnsForwarderInstall(
                s_onToken, s_onFail, s_onWillPresent, s_onDidReceiveResponse, s_onColdStart);
        }

        [AOT.MonoPInvokeCallback(typeof(TokenCallback))]
        private static void OnToken(string hexToken)
        {
            var plugin = s_plugin;
            if (plugin == null || string.IsNullOrEmpty(hexToken))
            {
                return;
            }

            // A malformed hex string would throw synchronously across the
            // native->managed callback boundary (UB under IL2CPP); catch it so a
            // native callback never crashes the app. `_ =` only leaves the async
            // RPC's own faults unobserved.
            byte[] deviceToken;
            try
            {
                deviceToken = HexToBytes(hexToken);
            }
            catch (System.FormatException)
            {
                return;
            }
            catch (System.OverflowException)
            {
                return;
            }

            // Fire-and-forget: the RPC resolves a pending GetTokenAsync and raises
            // TokenRefreshed.
            _ = plugin.NotifyDidRegisterForRemoteNotificationsAsync(deviceToken);
        }

        [AOT.MonoPInvokeCallback(typeof(FailCallback))]
        private static void OnFail(string errorDescription)
        {
            var plugin = s_plugin;
            if (plugin == null)
            {
                return;
            }

            _ = plugin.NotifyDidFailToRegisterAsync(errorDescription ?? string.Empty);
        }

        [AOT.MonoPInvokeCallback(typeof(NotificationCallback))]
        private static void OnWillPresent(string notificationJson)
        {
            var plugin = s_plugin;
            if (plugin == null)
            {
                return;
            }

            // Fire-and-forget: raises NotificationPresented on the foreground path.
            _ = plugin.NotifyWillPresentNotificationAsync(ParseNotification(notificationJson));
        }

        [AOT.MonoPInvokeCallback(typeof(ResponseCallback))]
        private static void OnDidReceiveResponse(string notificationJson, string actionIdentifier)
        {
            var plugin = s_plugin;
            if (plugin == null)
            {
                return;
            }

            // Raises NotificationOpened on the tap path; the actionIdentifier
            // rides along (default tap or a custom action).
            _ = plugin.NotifyDidReceiveResponseAsync(
                ParseNotification(notificationJson),
                actionIdentifier ?? string.Empty);
        }

        [AOT.MonoPInvokeCallback(typeof(ColdStartCallback))]
        private static void OnColdStart(string userInfoJson)
        {
            var plugin = s_plugin;
            if (plugin == null)
            {
                return;
            }

            // An empty relay is the native side reporting "this launch carried no
            // notification" — nothing to seed, the cold-start state is known now.
            if (string.IsNullOrEmpty(userInfoJson))
            {
                s_coldStartRelay.TrySetResult(true);
                return;
            }

            // Seeds the cold-start buffer with the raw launch payload so a later
            // GetColdStartNotificationAsync returns the notification that launched
            // the app from a terminated state. The addon re-checks the payload, so a
            // non-object relay is a no-op. ColdStartRelayed completes only after the
            // RPC finished seeding, so an awaiting caller never out-races the buffer.
            _ = RelayColdStart(plugin, userInfoJson);
        }

        private static async System.Threading.Tasks.Task RelayColdStart(
            IAPNSPlugin plugin, string userInfoJson)
        {
            await plugin.NotifyColdStartNotificationAsync(userInfoJson);
            s_coldStartRelay.TrySetResult(true);
        }

        // Deserialises the native JSON (ApnsNotification shape, built by
        // AxylApnsNotificationJson to mirror the addon's own serializer) into the DTO
        // with the SDK's generated FromJsonString, so the template needs no external
        // JSON library. A malformed payload maps to an empty notification rather than
        // throwing, so a native callback never crashes the app.
        private static ApnsNotification ParseNotification(string notificationJson)
        {
            if (string.IsNullOrEmpty(notificationJson))
            {
                return new ApnsNotification();
            }

            try
            {
                return ApnsNotification.FromJsonString(notificationJson);
            }
            catch (Hive.Axyl.Core.Serialization.JsonParseException)
            {
                return new ApnsNotification();
            }
        }

        private static byte[] HexToBytes(string hex)
        {
            var length = hex.Length / 2;
            var bytes = new byte[length];
            for (var i = 0; i < length; i++)
            {
                bytes[i] = System.Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }
#else
        /// <summary>No-op off Apple players (Editor / other platforms).</summary>
        public static void Install(IAPNSPlugin plugin)
        {
        }

        /// <summary>Already complete off Apple players — there is no cold start to wait for.</summary>
        public static System.Threading.Tasks.Task ColdStartRelayed
            => System.Threading.Tasks.Task.CompletedTask;
#endif
    }
}

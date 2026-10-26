// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// The WebGL bridge to Apple's JS SDK popup (<c>AppleID.auth.signIn</c> with <c>usePopup</c>).
    /// Hands back the raw result as JSON; <see cref="AppleWebAuthorization"/> reads it.
    /// </summary>
    /// <remarks>
    /// Apple delivers the result only to a page whose origin is registered on the Services ID,
    /// together with the redirect URI. Call <see cref="Preload"/> when the sign-in screen opens so
    /// the script is loaded by the time the player clicks: the popup is only allowed inside that
    /// click, so nothing may await between the click and <see cref="SignInAsync"/>.
    /// </remarks>
    internal static class AppleWebPopupBridge
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ResultCallback(int requestId, IntPtr utf8Json);

        // One sign-in at a time, and each carries an id: a popup the player closed can still report
        // in after the next sign-in started, and that late result must not complete the new one.
        private static int s_lastRequestId;
        private static int s_pendingId;
        private static TaskCompletionSource<string> s_pending;
        private static readonly ResultCallback s_onResult = OnResult;

        /// <summary>
        /// Starts loading Apple's JS SDK into the page. Safe to call more than once; a no-op off WebGL.
        /// </summary>
        public static void Preload()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            NativePreload();
#endif
        }

        /// <summary>
        /// Opens Apple's popup and returns the <c>signIn()</c> result as JSON: the authorization
        /// (code, id_token, state) and, on the first authorization, the user's name and email; or
        /// <c>{"error": ...}</c> when Apple JS reported a failure or the user closed the popup.
        /// Null when Apple's script is not loaded in the page, and always null off WebGL. A sign-in
        /// still pending when the next one starts ends as canceled.
        /// </summary>
        public static Task<string> SignInAsync(
            string clientId, string scope, string redirectUri, string state, string nonceHash,
            CancellationToken cancellationToken = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<string>(cancellationToken);
            }

            var pending = Begin(out var requestId);
            if (NativeSignIn(requestId, clientId, scope ?? string.Empty,
                redirectUri, state, nonceHash, s_onResult) <= 0)
            {
                Forget(requestId);
                return Task.FromResult<string>(null);
            }

            return Await(pending, requestId, cancellationToken);
#else
            return Task.FromResult<string>(null);
#endif
        }

        // The pending-request bookkeeping below does not touch the page, so EditMode tests drive it
        // directly. It is static state: tests reset it and run one at a time.

        /// <summary>Registers a new pending sign-in, ending any previous one as canceled.</summary>
        internal static TaskCompletionSource<string> Begin(out int requestId)
        {
            // Run caller continuations asynchronously so cancellation cannot re-enter Begin inline.
            var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var previous = s_pending;
            // Keep zero reserved for no request, including across integer wraparound.
            s_lastRequestId = s_lastRequestId == int.MaxValue ? 1 : s_lastRequestId + 1;
            requestId = s_lastRequestId;
            s_pendingId = requestId;
            s_pending = pending;
            previous?.TrySetCanceled();
            return pending;
        }

        /// <summary>
        /// Completes the pending sign-in with the result the page delivered, if the id is the one
        /// pending; a result for any other id is dropped.
        /// </summary>
        internal static bool Deliver(int requestId, string json)
        {
            if (requestId != s_pendingId || s_pending == null)
            {
                return false;
            }

            var pending = s_pending;
            Forget(requestId);
            return pending.TrySetResult(json ?? string.Empty);
        }

        /// <summary>Drops the pending registration for the id, if it is still the one pending.</summary>
        internal static void Forget(int requestId)
        {
            if (requestId == s_pendingId)
            {
                s_pending = null;
                s_pendingId = 0;
            }
        }

        /// <summary>Waits for the sign-in, ending it as canceled and dropping it when the caller cancels.</summary>
        internal static async Task<string> Await(
            TaskCompletionSource<string> pending, int requestId, CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => pending.TrySetCanceled()))
            {
                try
                {
                    return await pending.Task;
                }
                finally
                {
                    Forget(requestId);
                }
            }
        }

        /// <summary>Clears the pending state between tests.</summary>
        internal static void ResetForTests(int lastRequestId = 0)
        {
            s_pending = null;
            s_pendingId = 0;
            s_lastRequestId = lastRequestId;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [AOT.MonoPInvokeCallback(typeof(ResultCallback))]
#endif
        internal static void OnResult(int requestId, IntPtr utf8Json)
        {
            try
            {
                // JS owns the buffer and frees it as soon as this callback returns.
                Deliver(requestId, Marshal.PtrToStringUTF8(utf8Json) ?? string.Empty);
            }
            catch
            {
                // Never unwind through the reverse P/Invoke boundary. An unreadable response
                // completes as a parser failure instead of leaving the caller waiting.
                Deliver(requestId, string.Empty);
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "HiveAxylRecipe_AppleWebSignIn_Preload")]
        private static extern void NativePreload();

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "HiveAxylRecipe_AppleWebSignIn_SignIn")]
        private static extern int NativeSignIn(
            int requestId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string clientId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string scope,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string redirectUri,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string state,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string nonceHash,
            ResultCallback callback);
#endif
    }
}

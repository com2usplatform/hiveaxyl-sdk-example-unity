// Copyright (c) Com2uS Platform Corp. All rights reserved.

mergeInto(LibraryManager.library, {
    // Loads Apple's JS SDK once. Called when the sign-in screen opens so the script is ready by
    // the time the player clicks; the popup below is only allowed inside that click.
    HiveAxylRecipe_AppleWebSignIn_Preload: function () {
        if (window.AppleID || document.getElementById('hive-axyl-appleid-js')) return;
        var script = document.createElement('script');
        script.id = 'hive-axyl-appleid-js';
        script.src = 'https://appleid.cdn-apple.com/appleauth/static/jsapi/appleid/1/en_US/appleid.auth.js';
        // A failed load leaves nothing behind, so the next Preload() tries again.
        script.onerror = function () { script.remove(); };
        document.head.appendChild(script);
    },

    // Opens Apple's popup and hands the signIn() result (or {error}) back as JSON through the
    // callback, tagged with the request id so a late result from an earlier popup is told apart.
    // Returns 1 when the popup was requested, 0 when the SDK is not loaded. Synchronous SDK errors use the callback.
    // Apple delivers the result only to a page whose origin is registered on the Services ID,
    // together with the redirect URI.
    HiveAxylRecipe_AppleWebSignIn_SignIn: function (
        requestId, clientIdPtr, scopePtr, redirectPtr, statePtr, noncePtr, callback) {
        if (!window.AppleID || !window.AppleID.auth) return 0;
        var deliver = function (obj) {
            var json = JSON.stringify(obj);
            var size = lengthBytesUTF8(json) + 1;
            var ptr = _malloc(size);
            stringToUTF8(json, ptr, size);
            try {
                {{{ makeDynCall('vii', 'callback') }}}(requestId, ptr);
            } finally {
                _free(ptr);
            }
        };
        try {
            window.AppleID.auth.init({
                clientId: UTF8ToString(clientIdPtr),
                redirectURI: UTF8ToString(redirectPtr),
                state: UTF8ToString(statePtr),
                nonce: UTF8ToString(noncePtr),
                usePopup: true
            });
            // init retains nonempty scopes globally. Keep scopes in the per-request config so
            // a later login with an empty scope does not inherit this login's permissions.
            window.AppleID.auth.signIn({ scope: UTF8ToString(scopePtr).trim() }).then(
                function (result) { deliver(result); },
                function (failure) { deliver({ error: failure && failure.error ? failure.error : String(failure) }); });
            return 1;
        } catch (_) {
            deliver({ error: 'browser_signin_failed' });
            return 1;
        }
    }
});

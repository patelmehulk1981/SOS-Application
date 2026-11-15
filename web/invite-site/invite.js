// Invite landing page for people who open https://<host>/i/<code> without the app installed
// (with the app installed, Android App Links / iOS Universal Links open the app directly).
// The code is validated against the API's alphabet and only ever written with textContent.
(function () {
  'use strict';

  // --- Configure per deployment -------------------------------------------------------------
  var ANDROID_PACKAGE = 'com.communitysos.app';
  var IOS_APP_STORE_URL = 'https://apps.apple.com/app/id0000000000';
  var APP_SCHEME = 'communitysos';
  // -------------------------------------------------------------------------------------------

  var CODE_PATTERN = /^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{10}$/;
  var match = /^\/i\/([^/?#]+)\/?$/.exec(window.location.pathname);
  var code = match ? decodeURIComponent(match[1]).trim().toUpperCase() : '';

  if (!CODE_PATTERN.test(code)) {
    document.getElementById('invalid').hidden = false;
    return;
  }

  document.getElementById('valid').hidden = false;
  document.getElementById('code').textContent = code.slice(0, 5) + ' ' + code.slice(5);
  document.getElementById('open').href = APP_SCHEME + '://i/' + code;
  // Google Play passes `referrer` to the app on first launch (Play Install Referrer API).
  document.getElementById('play').href =
    'https://play.google.com/store/apps/details?id=' + ANDROID_PACKAGE +
    '&referrer=' + encodeURIComponent('invite_code=' + code);
  document.getElementById('appstore').href = IOS_APP_STORE_URL;

  var copy = document.getElementById('copy');
  copy.addEventListener('click', function () {
    if (!navigator.clipboard) return;
    navigator.clipboard.writeText(code).then(function () {
      copy.textContent = 'Copied';
      setTimeout(function () { copy.textContent = 'Copy code'; }, 2000);
    });
  });
})();

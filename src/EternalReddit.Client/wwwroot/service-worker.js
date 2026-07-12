// Minimal service worker. It exists only so the browser treats EternalReddit as
// an installable PWA - it deliberately does NOT cache anything, so a deploy is
// never served stale from an old cache.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => { /* network pass-through; no caching */ });

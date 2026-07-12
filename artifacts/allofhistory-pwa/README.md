# r/AllOfHistory — PWA

An installable Progressive Web App version of the "submit a prompt, get a fictional
historical-figures Reddit thread" generator.

## What's in this folder

- `index.html` — the app itself (vanilla HTML/CSS/JS, no build step)
- `manifest.json` — PWA metadata (name, icons, standalone display mode)
- `sw.js` — service worker, caches the app shell so the UI loads offline
- `icons/icon-192.png`, `icons/icon-512.png` — app icons

## How to run it

PWAs need to be served over HTTP(S) — opening `index.html` directly via `file://`
will not register the service worker or allow installation.

Quickest local test:

```
cd allofhistory-pwa
python3 -m http.server 8080
```

Then open `http://localhost:8080` in Chrome, Edge, or another PWA-capable browser.
You should see an "Install" banner appear (or use the browser's own install icon
in the address bar).

To make it installable from anywhere, deploy the folder as static files to any
static host (GitHub Pages, Netlify, Vercel, Cloudflare Pages, etc.) — no server
code required except for the proxy noted below.

## About the API key

This app calls the Anthropic API directly from the browser using a key you enter
yourself. That key is stored only in `localStorage` on your device — it isn't sent
anywhere except `api.anthropic.com`.

**Important caveat:** browsers frequently block direct `fetch()` calls to
`api.anthropic.com` due to CORS, regardless of the `anthropic-dangerous-direct-browser-access`
header. If thread generation fails, the reliable fix is to run a small backend proxy
(a few lines in Node/Express, Cloudflare Worker, or similar) that holds your API key
server-side and forwards requests to Anthropic — then point this app's fetch call at
your proxy's URL instead of `api.anthropic.com` directly. Never ship a real product
with a raw API key sitting in client-side `localStorage`; that's fine for personal,
local experimentation only.

## Content safety built in

The system prompt baked into the generator explicitly instructs the model to:
- avoid any figure whose primary legacy is atrocity, violent oppression, or founding
  a hate group
- keep humor affectionate rather than mean-spirited
- avoid fabricating quotes that could be mistaken for real historical statements

This is enforced by the prompt, not by external filtering — it's a reasonable
default but not a hard guarantee.

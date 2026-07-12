# EternalReddit round-robin proxy

A tiny Cloudflare Worker that holds the Grok / Claude / ChatGPT API keys
server-side, so the static site never sees a key and visitors need none of
their own. It also fixes the xAI (Grok) browser-CORS problem, because
server-to-server calls have no CORS.

## What it does

- `GET /providers` -> `{ "providers": ["grok","claude","chatgpt"] }` (only the
  ones you configured a key for). The site calls this to build the rotation.
- `POST /chat` with `{ provider, system, user, max_tokens }` -> `{ text }`.
  The site calls this once per comment, rotating the `provider`.

Keys live in Cloudflare's secret store. They are never in the repo, never in a
browser, and never returned by the Worker.

## Deploy (once)

Prereqs: a free Cloudflare account and Node.js.

```sh
cd proxy
npm install -g wrangler      # or: npx wrangler ...
wrangler login

# Set whichever keys you have (set any subset - rotation uses what's present):
wrangler secret put GROK_KEY      # paste your xAI key
wrangler secret put CLAUDE_KEY    # paste your Anthropic key
wrangler secret put OPENAI_KEY    # paste your OpenAI key

wrangler deploy
```

`wrangler deploy` prints a URL like `https://eternalreddit-proxy.<you>.workers.dev`.

## Point the site at it

Open the generator (the site's landing page), and in the **Proxy URL** field at
the top paste the Worker URL:

```
https://eternalreddit-proxy.<you>.workers.dev
```

When that field is set, the site runs the whole round-robin through the Worker
and **ignores the per-provider key fields** - anyone who opens the site can
generate threads with no keys of their own.

## Options

- **Lock CORS to your site.** By default the Worker allows any origin. To
  restrict it, set `ALLOWED_ORIGIN` in `wrangler.toml` (`[vars]`) to your Pages
  origin (e.g. `https://sharpninja.github.io`) and redeploy.
- **Change models.** Defaults are `grok-3`, `claude-opus-4-8`, `gpt-4o`.
  Override per provider via `GROK_MODEL` / `CLAUDE_MODEL` / `OPENAI_MODEL` in
  `wrangler.toml` `[vars]`, or as secrets, then redeploy.
- **Rotate/revoke a key.** `wrangler secret put <NAME>` again to replace it, or
  `wrangler secret delete <NAME>` to drop that provider from the rotation.

## Cost note

The Worker itself runs on Cloudflare's free tier. Provider token costs are
billed to whatever keys you configured - i.e. **your** keys pay for every
visitor's generations, since visitors bring none. If you expose the site
publicly, consider `ALLOWED_ORIGIN` plus your own rate limiting.

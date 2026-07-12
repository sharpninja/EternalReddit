// EternalReddit round-robin proxy (Cloudflare Worker)
//
// Holds the Grok / Claude / ChatGPT API keys as Worker secrets so the static
// site never sees them and visitors need no keys of their own. Also sidesteps
// browser CORS for xAI (server-to-server has no CORS).
//
// Endpoints:
//   GET  /providers  -> { providers: [ids that have a key configured] }
//   POST /chat       -> { provider, system, user, max_tokens } => { text } | { error }
//
// Secrets (set with `wrangler secret put <NAME>`):
//   GROK_KEY, CLAUDE_KEY, OPENAI_KEY  (set any subset; rotation uses what's present)
// Optional vars (wrangler.toml [vars] or secrets):
//   GROK_MODEL, CLAUDE_MODEL, OPENAI_MODEL, ALLOWED_ORIGIN

const DEFAULT_MODELS = { grok: "grok-3", claude: "claude-opus-4-8", chatgpt: "gpt-4o" };

function providerConfig(id, env) {
  if (id === "claude") return { kind: "anthropic", endpoint: "https://api.anthropic.com/v1/messages", key: env.CLAUDE_KEY, model: env.CLAUDE_MODEL || DEFAULT_MODELS.claude };
  if (id === "grok")    return { kind: "openai",    endpoint: "https://api.x.ai/v1/chat/completions",  key: env.GROK_KEY,   model: env.GROK_MODEL   || DEFAULT_MODELS.grok };
  if (id === "chatgpt") return { kind: "openai",    endpoint: "https://api.openai.com/v1/chat/completions", key: env.OPENAI_KEY, model: env.OPENAI_MODEL || DEFAULT_MODELS.chatgpt };
  return null;
}

function corsHeaders(env) {
  return {
    "Access-Control-Allow-Origin": env.ALLOWED_ORIGIN || "*",
    "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
    "Access-Control-Allow-Headers": "content-type",
    "Access-Control-Max-Age": "86400",
  };
}

function json(obj, status, env) {
  return new Response(JSON.stringify(obj), {
    status: status || 200,
    headers: { "content-type": "application/json", ...corsHeaders(env) },
  });
}

async function callProvider(p, system, user, maxTokens) {
  let res;
  if (p.kind === "anthropic") {
    res = await fetch(p.endpoint, {
      method: "POST",
      headers: { "content-type": "application/json", "x-api-key": p.key, "anthropic-version": "2023-06-01" },
      body: JSON.stringify({ model: p.model, max_tokens: maxTokens, system, messages: [{ role: "user", content: user }] }),
    });
    if (!res.ok) throw new Error(res.status + ": " + (await res.text()).slice(0, 180));
    const d = await res.json();
    const t = (d.content || []).find((b) => b.type === "text");
    if (!t) throw new Error("no text in response");
    return t.text;
  }
  // OpenAI-compatible (Grok + ChatGPT)
  res = await fetch(p.endpoint, {
    method: "POST",
    headers: { "content-type": "application/json", "authorization": "Bearer " + p.key },
    body: JSON.stringify({ model: p.model, max_tokens: maxTokens, messages: [{ role: "system", content: system }, { role: "user", content: user }] }),
  });
  if (!res.ok) throw new Error(res.status + ": " + (await res.text()).slice(0, 180));
  const d = await res.json();
  const c = d.choices && d.choices[0] && d.choices[0].message && d.choices[0].message.content;
  if (!c) throw new Error("no content in response");
  return c;
}

export default {
  async fetch(req, env) {
    const url = new URL(req.url);

    if (req.method === "OPTIONS") return new Response(null, { status: 204, headers: corsHeaders(env) });

    if (url.pathname === "/providers") {
      const providers = ["grok", "claude", "chatgpt"].filter((id) => providerConfig(id, env).key);
      return json({ providers }, 200, env);
    }

    if (url.pathname === "/chat" && req.method === "POST") {
      let body;
      try { body = await req.json(); } catch (e) { return json({ error: "invalid JSON body" }, 400, env); }
      const p = providerConfig(body.provider, env);
      if (!p) return json({ error: "unknown provider: " + body.provider }, 400, env);
      if (!p.key) return json({ error: body.provider + " is not configured on this proxy" }, 400, env);
      const maxTokens = Math.min(2000, Math.max(64, body.max_tokens || 600));
      try {
        const text = await callProvider(p, body.system || "", body.user || "", maxTokens);
        return json({ text }, 200, env);
      } catch (e) {
        return json({ error: String((e && e.message) || e) }, 502, env);
      }
    }

    return json({ ok: true, service: "eternalreddit-proxy" }, 200, env);
  },
};

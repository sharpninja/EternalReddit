using System.Net;
using System.Text;

namespace EternalSocial.Proxy;

/// <summary>Server-rendered pages: the landing hub, the coming-soon page, and the admin console.</summary>
public static class GatewayHtml
{
    private const string Css = """
        :root { --bg:#0e1113; --card:#1a1d1f; --line:#2e3236; --ink:#d7dadc; --dim:#8f9294; --accent:#ff4500; }
        @media (prefers-color-scheme: light) {
            :root { --bg:#f6f7f8; --card:#ffffff; --line:#e2e4e8; --ink:#1a1a1b; --dim:#7c7f83; }
        }
        * { box-sizing: border-box; }
        body { margin:0; font-family:'Segoe UI',Helvetica,Arial,sans-serif; background:var(--bg); color:var(--ink); }
        a { color: var(--accent); text-decoration: none; }
        a:hover { text-decoration: underline; }
        .wrap { max-width:640px; margin:0 auto; padding:28px 16px; }
        h1 { font-size:2rem; margin:0 0 4px; } h1 .accent { color:var(--accent); }
        .tag { color:var(--dim); margin:0 0 24px; }
        .topline { display:flex; align-items:center; gap:12px; margin-bottom:18px; font-size:0.9rem; }
        .spacer { flex:1; }
        .pillbtn { background:var(--accent); color:#fff !important; font-weight:700; padding:6px 16px; border-radius:999px; border:none; cursor:pointer; font-size:0.85rem; }
        .net { display:flex; align-items:center; gap:14px; background:var(--card); border:1px solid var(--line);
               border-radius:10px; padding:16px 18px; margin-bottom:12px; color:var(--ink) !important; }
        .net:hover { border-color:var(--accent); text-decoration:none; }
        .glyph { font-size:1.5rem; width:44px; height:44px; display:flex; align-items:center; justify-content:center;
                 background:var(--bg); border:1px solid var(--line); border-radius:10px; }
        .name { font-weight:800; } .desc { color:var(--dim); font-size:0.86rem; } .grow { flex:1; min-width:0; }
        .pill { font-size:0.68rem; font-weight:800; text-transform:uppercase; padding:2px 9px; border-radius:999px; }
        .live { background:var(--accent); color:#fff; } .soon { border:1px solid var(--line); color:var(--dim); }
        .off { border:1px solid var(--line); color:var(--dim); text-decoration:line-through; }
        .foot { color:var(--dim); font-size:0.75rem; margin-top:22px; }
        table { width:100%; border-collapse:collapse; font-size:0.85rem; }
        th, td { text-align:left; padding:7px 8px; border-top:1px solid var(--line); vertical-align:middle; }
        th { color:var(--dim); font-size:0.72rem; text-transform:uppercase; }
        input[type=text] { width:100%; background:var(--bg); color:var(--ink); border:1px solid var(--line); border-radius:6px; padding:6px 8px; font:inherit; }
        .card { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:16px 18px; margin-bottom:16px; }
        .card h2 { margin:0 0 10px; font-size:1.05rem; }
        .mini { font-size:0.78rem; color:var(--dim); }
        .danger { background:#c73438; }
        .logrow { font-family:Consolas,monospace; font-size:0.76rem; padding:3px 0; border-top:1px solid var(--line); }
        .lvl-Warning { color:#f5a623; } .lvl-Error, .lvl-Critical { color:#e5484d; }
        #status { color:var(--accent); min-height:1.2em; font-size:0.85rem; }
        """;

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string Landing(IReadOnlyList<ProxyRoute> routes, string? userName, bool isAdmin, bool loginAvailable)
    {
        var sb = new StringBuilder();
        sb.Append($"""
            <!DOCTYPE html><html lang="en"><head><meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>EternalSocial</title><style>{Css}</style></head><body><div class="wrap">
            <div class="topline">
            """);
        if (userName is not null)
        {
            sb.Append($"<span>Hey, <b>{E(userName)}</b></span>");
            if (isAdmin) sb.Append(" · <a href=\"/admin\">Admin</a>");
            sb.Append("<span class=\"spacer\"></span><form method=\"post\" action=\"/logout\" style=\"margin:0\"><button class=\"pillbtn\" type=\"submit\">Log out</button></form>");
        }
        else
        {
            sb.Append("<span class=\"spacer\"></span>");
            sb.Append(loginAvailable
                ? "<a class=\"pillbtn\" href=\"/login\">Log in with Google</a>"
                : "<span class=\"mini\">login not configured</span>");
        }
        sb.Append("""
            </div>
            <h1>Eternal<span class="accent">Social</span></h1>
            <p class="tag">Social networks for the permanently unavailable.</p>
            """);

        foreach (var r in routes)
        {
            var live = r.Enabled && r.Upstream.Length > 0;
            var pill = !r.Enabled ? "<span class=\"pill off\">Offline</span>"
                     : live ? "<span class=\"pill live\">Live</span>"
                     : "<span class=\"pill soon\">Soon</span>";
            var glyph = r.Prefix switch { "/r" => "&#128220;", "/x" => "&#128038;", "/d" => "&#127908;", _ => "&#127760;" };
            sb.Append($"""
                <a class="net" href="{E(r.Prefix)}/">
                    <span class="glyph">{glyph}</span>
                    <span class="grow"><div class="name">{E(r.Title)}</div><div class="desc">{E(r.Description)}</div></span>
                    {pill}
                </a>
                """);
        }

        sb.Append("<p class=\"foot\">One tunnel, many afterlives.</p></div></body></html>");
        return sb.ToString();
    }

    public static string Soon(string title) => $$"""
        <!DOCTYPE html><html lang="en"><head><meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        <title>Coming soon - EternalSocial</title><style>{{Css}}
        body { min-height:100vh; display:flex; align-items:center; justify-content:center; text-align:center; }
        </style></head><body><div class="card" style="max-width:420px">
        <h1>Not <span class="accent">yet</span> eternal</h1>
        <p class="tag">{{E(title)}} hasn't launched. The dead are still onboarding.</p>
        <a href="/">&larr; Back to EternalSocial</a>
        </div></body></html>
        """;

    public static string AdminPage() => $$$"""
        <!DOCTYPE html><html lang="en"><head><meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        <title>Gateway Admin - EternalSocial</title><style>{{{Css}}}</style></head><body><div class="wrap">
        <div class="topline"><a href="/">&larr; EternalSocial</a><span class="spacer"></span>
        <form method="post" action="/logout" style="margin:0"><button class="pillbtn" type="submit">Log out</button></form></div>
        <h1>Gateway <span class="accent">Admin</span></h1>
        <p id="status"></p>

        <div class="card">
            <h2>Proxy paths</h2>
            <p class="mini">Prefix routes to an internal upstream (e.g. http://eternalreddit:8080). Blank upstream = coming-soon page. Changes apply immediately. /r is protected.</p>
            <table id="routes"><thead><tr><th>Prefix</th><th>Title</th><th>Upstream</th><th>On</th><th></th></tr></thead><tbody></tbody></table>
            <h2 style="margin-top:16px">Add path</h2>
            <table><tr>
                <td style="width:90px"><input type="text" id="np" placeholder="/x" /></td>
                <td><input type="text" id="nt" placeholder="Title" /></td>
                <td><input type="text" id="nu" placeholder="http://container:8080 (blank = soon)" /></td>
                <td style="width:70px"><button class="pillbtn" onclick="addRoute()">Add</button></td>
            </tr></table>
        </div>

        <div class="card">
            <h2>Gateway logs <button class="pillbtn" style="float:right" onclick="loadLogs()">Refresh</button></h2>
            <div id="logs"></div>
        </div>
        </div>
        <script>
        const status = (m) => document.getElementById('status').textContent = m;
        async function api(url, opts) {
            const res = await fetch(url, Object.assign({ headers: { 'Content-Type': 'application/json' } }, opts));
            if (!res.ok) throw new Error(res.status + ' ' + await res.text());
            return res.status === 204 ? null : res.json();
        }
        function row(r) {
            const tr = document.createElement('tr');
            const protectedRoute = r.prefix === '/r';
            tr.innerHTML = `
                <td><b>${r.prefix}</b></td>
                <td><input type="text" value="${r.title.replace(/"/g,'&quot;')}" data-f="title"></td>
                <td><input type="text" value="${r.upstream.replace(/"/g,'&quot;')}" data-f="upstream" ${protectedRoute ? 'title="/r upstream is editable - be careful"' : ''}></td>
                <td><input type="checkbox" ${r.enabled ? 'checked' : ''} data-f="enabled" ${protectedRoute ? 'disabled' : ''}></td>
                <td style="white-space:nowrap">
                    <button class="pillbtn" onclick="saveRoute(this)">Save</button>
                    ${protectedRoute ? '' : '<button class="pillbtn danger" onclick="delRoute(this)">Delete</button>'}
                </td>`;
            tr.dataset.prefix = r.prefix;
            tr.dataset.description = r.description ?? '';
            return tr;
        }
        async function loadRoutes() {
            const routes = await api('/_gateway/routes');
            const body = document.querySelector('#routes tbody');
            body.innerHTML = '';
            routes.forEach(r => body.appendChild(row(r)));
        }
        async function saveRoute(btn) {
            const tr = btn.closest('tr');
            const get = f => tr.querySelector(`[data-f=${f}]`);
            try {
                await api('/_gateway/routes', { method: 'PUT', body: JSON.stringify({
                    prefix: tr.dataset.prefix,
                    title: get('title').value,
                    description: tr.dataset.description,
                    upstream: get('upstream').value.trim(),
                    enabled: get('enabled').checked
                })});
                status('Saved ' + tr.dataset.prefix + '.');
                await loadRoutes();
            } catch (e) { status('Save failed: ' + e.message); }
        }
        async function delRoute(btn) {
            const tr = btn.closest('tr');
            if (!confirm('Delete ' + tr.dataset.prefix + '?')) return;
            try {
                await api('/_gateway/routes/' + encodeURIComponent(tr.dataset.prefix.substring(1)), { method: 'DELETE' });
                status('Deleted ' + tr.dataset.prefix + '.');
                await loadRoutes();
            } catch (e) { status('Delete failed: ' + e.message); }
        }
        async function addRoute() {
            try {
                await api('/_gateway/routes', { method: 'PUT', body: JSON.stringify({
                    prefix: document.getElementById('np').value.trim(),
                    title: document.getElementById('nt').value.trim(),
                    upstream: document.getElementById('nu').value.trim(),
                    enabled: true
                })});
                status('Added.');
                document.getElementById('np').value = document.getElementById('nt').value = document.getElementById('nu').value = '';
                await loadRoutes();
            } catch (e) { status('Add failed: ' + e.message); }
        }
        async function loadLogs() {
            const logs = await api('/_gateway/logs?count=100');
            document.getElementById('logs').innerHTML = logs.map(l =>
                `<div class="logrow lvl-${l.level}">${l.utc.substring(11,19)} [${l.level}] ${l.category}: ${l.message.replace(/</g,'&lt;')}</div>`).join('');
        }
        loadRoutes().catch(e => status('Load failed: ' + e.message));
        loadLogs().catch(() => {});
        </script></body></html>
        """;
}

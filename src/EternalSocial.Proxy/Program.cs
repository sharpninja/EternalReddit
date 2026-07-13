using System.Security.Claims;
using EternalSocial.Proxy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// --- In-app log capture (surfaced on /admin) ---
var logSink = new GatewayLogSink();
builder.Services.AddSingleton(logSink);
builder.Logging.AddProvider(new GatewayLoggerProvider(logSink));

// --- Route table (LiteDB) ---
builder.Services.AddSingleton<GatewayDb>();
builder.Services.AddSingleton<IRouteStore, LiteDbRouteStore>();
builder.Services.AddSingleton<GatewayState>();

// Cookie encryption keys survive redeploys when the data volume is mounted.
if (Directory.Exists("/app/data"))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo("/app/data/keys"));

// --- Auth: the gateway owns the Google OIDC flow for every Eternal site ---
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleConfigured = !string.IsNullOrWhiteSpace(googleClientId);
var auth = builder.Services
    .AddAuthentication(o =>
    {
        // The cookie handler always mediates challenges: APIs get clean 401s and
        // pages bounce to /login, which explicitly challenges Google.
        o.DefaultScheme = "Cookies";
        o.DefaultChallengeScheme = "Cookies";
    })
    .AddCookie("Cookies", o =>
    {
        o.LoginPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromDays(365);
        o.SlidingExpiration = true;
        o.Events.OnSigningIn = ctx =>
        {
            ctx.Properties.IsPersistent = true;
            ctx.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(365);
            return Task.CompletedTask;
        };
        // Gateway API endpoints answer 401/403; pages redirect to login.
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/_gateway"))
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/_gateway"))
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            else ctx.Response.Redirect("/");
            return Task.CompletedTask;
        };
    });

if (googleConfigured)
{
    auth.AddOpenIdConnect("Google", o =>
    {
        o.Authority = "https://accounts.google.com";
        o.ClientId = googleClientId!;
        o.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "";
        o.ResponseType = "code";
        o.CallbackPath = "/signin-oidc";
        o.SaveTokens = true;
        o.UseTokenLifetime = false;
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.Scope.Add("email");
    });
}

var adminEmail = GatewayAdmin.ConfiguredEmail(builder.Configuration);
builder.Services.AddAuthorization(o =>
    o.AddPolicy(GatewayAdmin.PolicyName, p => p.RequireAssertion(ctx => GatewayAdmin.IsAdmin(ctx.User, adminEmail))));

// --- YARP: routes come from the store; identity flows downstream as headers ---
var gatewayKey = builder.Configuration["GATEWAY_KEY"];
builder.Services.AddReverseProxy()
    .LoadFromMemory(Array.Empty<RouteConfig>(), Array.Empty<ClusterConfig>())
    .AddTransforms(t => t.AddRequestTransform(ctx =>
    {
        var headers = ctx.ProxyRequest.Headers;
        // Never let a client smuggle identity headers through the gateway.
        headers.Remove("X-Gateway-Key");
        headers.Remove("X-Auth-UserId");
        headers.Remove("X-Auth-Name");
        headers.Remove("X-Auth-Email");

        if (!string.IsNullOrEmpty(gatewayKey))
        {
            headers.TryAddWithoutValidation("X-Gateway-Key", gatewayKey);
            var user = ctx.HttpContext.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    headers.TryAddWithoutValidation("X-Auth-UserId", userId);
                    var name = user.FindFirst("name")?.Value ?? user.FindFirst(ClaimTypes.Name)?.Value;
                    if (!string.IsNullOrEmpty(name)) headers.TryAddWithoutValidation("X-Auth-Name", name);
                    var email = user.FindFirst("email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;
                    if (!string.IsNullOrEmpty(email)) headers.TryAddWithoutValidation("X-Auth-Email", email);
                }
            }
        }
        return default;
    }));

var app = builder.Build();

// Seed the route table and push it into YARP.
var store = app.Services.GetRequiredService<IRouteStore>();
var state = app.Services.GetRequiredService<GatewayState>();
GatewaySeed.EnsureSeeded(store);
state.Reload(store);
var proxyConfig = app.Services.GetRequiredService<InMemoryConfigProvider>();
ApplyRoutes(state, proxyConfig);

// Behind ngrok: honor the tunnel's forwarded headers so OIDC builds https
// redirect URIs. The allow-lists must be explicitly CLEARED (an empty collection
// initializer keeps the loopback defaults and silently rejects the tunnel).
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

app.UseAuthentication();
app.UseAuthorization();

// Bare-prefix hits (e.g. /r) bounce to the canonical /r/ before proxying.
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    if (state.Routes.Any(r => r.Enabled && r.Upstream.Length > 0 && string.Equals(r.Prefix, path, StringComparison.OrdinalIgnoreCase)))
    {
        ctx.Response.Redirect(path + "/");
        return;
    }
    await next();
});

// --- Gateway-owned pages ---
app.MapGet("/", (HttpContext http, IConfiguration config) =>
{
    var name = http.User.Identity?.IsAuthenticated == true
        ? http.User.FindFirst("name")?.Value ?? http.User.Identity!.Name
        : null;
    var isAdmin = GatewayAdmin.IsAdmin(http.User, GatewayAdmin.ConfiguredEmail(config));
    return Results.Content(GatewayHtml.Landing(state.Routes, name, isAdmin, googleConfigured), "text/html; charset=utf-8");
});

app.MapGet("/login", (string? returnUrl) =>
{
    if (!googleConfigured) return Results.Text("Login is not configured on this gateway.", statusCode: StatusCodes.Status503ServiceUnavailable);
    var target = returnUrl is { Length: > 0 } r && r.StartsWith('/') && !r.StartsWith("//") ? r : "/";
    return Results.Challenge(new AuthenticationProperties { RedirectUri = target }, new[] { "Google" });
});

app.MapGet("/logout", async (HttpContext http) => { await http.SignOutAsync("Cookies"); return Results.Redirect("/"); });
app.MapPost("/logout", async (HttpContext http) => { await http.SignOutAsync("Cookies"); return Results.Redirect("/"); });

app.MapGet("/admin", () => Results.Content(GatewayHtml.AdminPage(), "text/html; charset=utf-8"))
    .RequireAuthorization(GatewayAdmin.PolicyName);

// --- Gateway API ---
app.MapGet("/_gateway/health", () => Results.Ok(new { status = "Healthy", routes = state.Routes.Count }));

app.MapGet("/_gateway/whoami", (HttpContext http, IConfiguration config) => Results.Ok(new
{
    authenticated = http.User.Identity?.IsAuthenticated ?? false,
    name = http.User.FindFirst("name")?.Value ?? http.User.Identity?.Name,
    isAdmin = GatewayAdmin.IsAdmin(http.User, GatewayAdmin.ConfiguredEmail(config))
}));

app.MapGet("/_gateway/routes", () => Results.Ok(state.Routes))
    .RequireAuthorization(GatewayAdmin.PolicyName);

app.MapPut("/_gateway/routes", (IRouteStore routes, ProxyRoute body, ILogger<Program> log) =>
{
    body.Prefix = (body.Prefix ?? "").Trim().ToLowerInvariant();
    body.Upstream = (body.Upstream ?? "").Trim();
    if (!GatewayMapper.IsValidPrefix(body.Prefix)) return Results.BadRequest("Prefix must look like /x (lowercase, no reserved paths).");
    if (!GatewayMapper.IsValidUpstream(body.Upstream)) return Results.BadRequest("Upstream must be blank or an absolute http(s) URL.");
    if (string.IsNullOrWhiteSpace(body.Title)) return Results.BadRequest("Title is required.");
    if (body.Prefix == "/r" && (!body.Enabled || body.Upstream.Length == 0))
        return Results.BadRequest("/r must stay enabled with an upstream (it hosts this admin's login audience).");

    routes.Upsert(body);
    state.Reload(routes);
    ApplyRoutes(state, proxyConfig);
    log.LogInformation("Route {Prefix} -> {Upstream} saved", body.Prefix, body.Upstream.Length == 0 ? "(soon)" : body.Upstream);
    return Results.Ok(body);
}).RequireAuthorization(GatewayAdmin.PolicyName);

app.MapDelete("/_gateway/routes/{slug}", (IRouteStore routes, string slug, ILogger<Program> log) =>
{
    var prefix = "/" + slug.Trim('/').ToLowerInvariant();
    if (prefix == "/r") return Results.BadRequest("/r cannot be deleted.");
    if (!routes.Delete(prefix)) return Results.NotFound();
    state.Reload(routes);
    ApplyRoutes(state, proxyConfig);
    log.LogInformation("Route {Prefix} deleted", prefix);
    return Results.NoContent();
}).RequireAuthorization(GatewayAdmin.PolicyName);

app.MapGet("/_gateway/logs", (int? count) => Results.Ok(logSink.Recent(count is > 0 ? count.Value : 200)))
    .RequireAuthorization(GatewayAdmin.PolicyName);

app.MapReverseProxy();

// Anything unmatched: a coming-soon page for configured-but-unlaunched prefixes, else 404.
app.MapFallback((HttpContext http) =>
{
    var path = http.Request.Path.Value ?? "";
    var soon = state.Routes.FirstOrDefault(r =>
        r.Enabled && r.Upstream.Length == 0
        && (string.Equals(r.Prefix, path, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(r.Prefix + "/", StringComparison.OrdinalIgnoreCase)));
    return soon is not null
        ? Results.Content(GatewayHtml.Soon(soon.Title), "text/html; charset=utf-8")
        : Results.NotFound();
});

app.Logger.LogInformation("EternalSocial gateway up: {Count} routes, google={Google}, forwarding identity={Forwarding}",
    state.Routes.Count, googleConfigured, !string.IsNullOrEmpty(gatewayKey));

app.Run();

static void ApplyRoutes(GatewayState state, InMemoryConfigProvider provider)
{
    var (routes, clusters) = GatewayMapper.Map(state.Routes);
    provider.Update(routes, clusters);
}

/// <summary>Exposed for WebApplicationFactory-based tests.</summary>
public partial class Program;

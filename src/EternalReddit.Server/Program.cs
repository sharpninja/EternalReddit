using System.Net.Http.Headers;
using System.Security.Claims;
using EternalReddit.Server.Auth;
using EternalReddit.Server.Data;
using EternalReddit.Server.Data.Seeding;
using EternalReddit.Server.Endpoints;
using EternalReddit.Server.Hubs;
using EternalReddit.Server.Services;
using EternalReddit.Server.Services.Ai;
using EternalReddit.Server.Services.Moderation;
using EternalReddit.Server.Services.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// --- MVC / hosting ---
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();

// --- In-app log capture (surfaced on the /logs page) ---
var logSink = new InMemoryLogSink();
builder.Services.AddSingleton(logSink);
builder.Logging.AddProvider(new InMemoryLoggerProvider(logSink));

// Persist DataProtection keys to the mounted /app/data volume (Production only)
// so auth cookies survive container restarts; otherwise every redeploy would
// regenerate the keys and log everyone out.
if (builder.Environment.IsProduction())
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo("/app/data/keys"));
}

// --- Data (LiteDB) ---
builder.Services.AddSingleton<LiteDbContext>();
builder.Services.AddSingleton<IPostStore, LiteDbPostStore>();
builder.Services.AddSingleton<IUserStore, LiteDbUserStore>();
builder.Services.AddSingleton<IModerationLogStore, LiteDbModerationLogStore>();
builder.Services.AddSingleton<IPeerGroupStore, LiteDbPeerGroupStore>();
builder.Services.AddSingleton<IFigureStore, LiteDbFigureStore>();
builder.Services.AddSingleton<ICommunityStore, LiteDbCommunityStore>();
builder.Services.AddSingleton<ISettingsStore, LiteDbSettingsStore>();

// --- Core services ---
builder.Services.AddSingleton<IClock, EternalReddit.Server.Services.SystemClock>();
builder.Services.AddSingleton<IRateLimiter>(sp =>
    new SlidingWindowRateLimiter(sp.GetRequiredService<IClock>(), limit: 1, window: TimeSpan.FromMinutes(1)));

builder.Services.AddSingleton<IModerationClassifier, HeuristicModerationClassifier>();
builder.Services.AddSingleton<IModerator, Moderator>();

// --- AI providers (round-robin). Keys come from configuration/environment. ---
builder.Services.AddSingleton<IAiProvider, ClaudeProvider>();
builder.Services.AddSingleton<IAiProvider, OpenAiProvider>();
builder.Services.AddSingleton<IAiProvider, GrokProvider>();
builder.Services.AddSingleton<IAiProvider, HuggingFaceProvider>();
builder.Services.AddSingleton<IReplyGenerator, ReplyGenerator>();
builder.Services.AddSingleton<IRosterService, RosterService>();

// --- Live updates (SignalR) ---
builder.Services.AddSignalR();
builder.Services.AddSingleton<IFeedNotifier, SignalRFeedNotifier>();

builder.Services.AddSingleton<IPostService, PostService>();
builder.Services.AddHostedService<AutoReplyBackgroundService>();
builder.Services.AddHostedService<CharacterPostBackgroundService>();

// --- Authentication (OIDC: Google, Microsoft, GitHub) ---
// A provider is only registered when its ClientId is configured, so the app
// runs (anonymous reads, /health) without any OAuth credentials set. An
// unregistered scheme with an empty ClientId would fail options validation on
// every request.
var authBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "Cookies";
        options.DefaultChallengeScheme = "Cookies";
    })
    .AddCookie("Cookies", options =>
    {
        // Long-lived, persistent sessions: stay logged in indefinitely, renewed on use.
        options.ExpireTimeSpan = TimeSpan.FromDays(365);
        options.SlidingExpiration = true;
        options.Events.OnSigningIn = ctx =>
        {
            ctx.Properties.IsPersistent = true;
            ctx.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(365);
            return Task.CompletedTask;
        };

        // The WASM client calls /api endpoints. An unauthenticated call must get
        // a 401/403, not a 302 redirect to a login page - that redirect falls
        // back to index.html (HTML, 200) and the client would then try to parse
        // HTML as JSON and throw.
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else
                ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            else
                ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

void AddOidc(string scheme, string authority, Action<OpenIdConnectOptions>? configure = null)
{
    var clientId = builder.Configuration[$"Authentication:{scheme}:ClientId"];
    if (string.IsNullOrWhiteSpace(clientId)) return;

    authBuilder.AddOpenIdConnect(scheme, options =>
    {
        options.Authority = authority;
        options.ClientId = clientId;
        options.ClientSecret = builder.Configuration[$"Authentication:{scheme}:ClientSecret"] ?? "";
        options.ResponseType = "code";
        options.SaveTokens = true;
        // Don't tie the cookie's lifetime to the ~1h id_token; the cookie manages its own long life.
        options.UseTokenLifetime = false;
        configure?.Invoke(options);
    });
}

AddOidc("Google", "https://accounts.google.com", o =>
{
    o.Scope.Add("openid");
    o.Scope.Add("profile");
    o.Scope.Add("email");
});
AddOidc("Microsoft", "https://login.microsoftonline.com/common/v2.0");

// GitHub is OAuth2, not OIDC (no discovery/id_token), so it uses a dedicated
// OAuth handler that fetches the profile from the GitHub user API.
var githubClientId = builder.Configuration["Authentication:GitHub:ClientId"];
if (!string.IsNullOrWhiteSpace(githubClientId))
{
    authBuilder.AddOAuth("GitHub", options =>
    {
        options.ClientId = githubClientId;
        options.ClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"] ?? "";
        options.CallbackPath = "/signin-github";
        options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
        options.TokenEndpoint = "https://github.com/login/oauth/access_token";
        options.UserInformationEndpoint = "https://api.github.com/user";
        options.SaveTokens = true;
        options.Scope.Add("read:user");
        options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
        options.ClaimActions.MapJsonKey(ClaimTypes.Name, "login");
        options.Events.OnCreatingTicket = async ctx =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ctx.Options.UserInformationEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AccessToken);
            request.Headers.UserAgent.ParseAdd("EternalReddit");
            using var response = await ctx.Backchannel.SendAsync(request, ctx.HttpContext.RequestAborted);
            response.EnsureSuccessStatusCode();
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            ctx.RunClaimActions(json.RootElement);
        };
    });
}

var adminEmail = AdminAccess.ConfiguredEmail(builder.Configuration);
builder.Services.AddAuthorization(options =>
    options.AddPolicy(AdminAccess.PolicyName, p => p.RequireAssertion(ctx => AdminAccess.IsAdmin(ctx.User, adminEmail))));

// Behind ngrok (or any reverse proxy): honor X-Forwarded-Proto/Host so the
// OIDC handler builds redirect URIs with the public https host
// (https://eternalsocial.ngrok.app/signin-oidc) instead of the internal one.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    // The proxy (ngrok container) is on an untrusted network from ASP.NET's
    // default view, so clear the allow-lists to accept its forwarded headers.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

// Must run before any middleware that reads the scheme/host (HTTPS redirect,
// authentication, OIDC challenge).
app.UseForwardedHeaders();

// Behind the EternalSocial proxy the app is mounted under a prefix (e.g. /r).
// UsePathBase strips it so routing/auth/static files all see root-relative paths
// while generated URLs (OIDC redirect_uri, cookies) include the prefix.
var pathBase = (builder.Configuration["PATH_BASE"] ?? "").TrimEnd('/');
if (!string.IsNullOrEmpty(pathBase))
    app.UsePathBase(pathBase);

if (app.Environment.IsDevelopment())
{
    var store = app.Services.GetRequiredService<IPostStore>();
    if (store.GetRecent(1).Count == 0)
        EternalReddit.Server.DevSeed.Seed(store);

    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
// Non-fingerprinted assets (index.html, app.css, manifest, icons) must revalidate on
// every load, or browsers heuristically cache them and users keep stale UI after a
// deploy. no-cache still allows cheap 304s via ETags.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        if (!path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "no-cache";
    }
});
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapPostEndpoints();
app.MapAdminEndpoints();
app.MapHub<FeedHub>("/hubs/feed");
app.MapHealthChecks("/health");

// --- Auth endpoints ---
app.MapGet("/login/{provider}", (string provider) =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, new[] { provider }));

app.MapGet("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync("Cookies");
    return Results.Redirect("/");
});
app.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync("Cookies");
    return Results.Redirect("/");
});

app.MapGet("/api/me", async (HttpContext http, IAuthenticationSchemeProvider schemes, IConfiguration config) =>
{
    var providers = (await schemes.GetAllSchemesAsync())
        .Where(s => s.Name != "Cookies" && s.Name != "Test")
        .Select(s => s.Name)
        .ToArray();
    return Results.Ok(new
    {
        authenticated = http.User.Identity?.IsAuthenticated ?? false,
        name = http.User.Identity?.Name,
        providers,
        isAdmin = AdminAccess.IsAdmin(http.User, AdminAccess.ConfiguredEmail(config))
    });
});

// SPA assets that carry root-anchored URLs are rewritten for PATH_BASE at first
// use: index.html (base href) via the fallback, the PWA manifest via /app.webmanifest.
string? LoadWebRootAsset(string name)
{
    var file = app.Environment.WebRootFileProvider.GetFileInfo(name);
    if (!file.Exists) return null;
    using var reader = new StreamReader(file.CreateReadStream());
    return reader.ReadToEnd();
}
var rewrittenIndex = new Lazy<string?>(() =>
    LoadWebRootAsset("index.html") is { } html ? EternalReddit.Server.PathBaseAssets.RewriteIndex(html, pathBase) : null);
var rewrittenManifest = new Lazy<string?>(() =>
    LoadWebRootAsset("manifest.webmanifest") is { } json ? EternalReddit.Server.PathBaseAssets.RewriteManifest(json, pathBase) : null);

app.MapGet("/app.webmanifest", (HttpResponse res) =>
{
    res.Headers.CacheControl = "no-cache";
    return rewrittenManifest.Value is { } manifest
        ? Results.Content(manifest, "application/manifest+json")
        : Results.NotFound();
});

app.MapFallback(async ctx =>
{
    if (rewrittenIndex.Value is null) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    ctx.Response.Headers.CacheControl = "no-cache";
    await ctx.Response.WriteAsync(rewrittenIndex.Value);
});

// Seed the default roster/communities (idempotent) BEFORE the purge, which validates
// existing replies against the roster.
RosterSeed.EnsureSeeded(
    app.Services.GetRequiredService<IPeerGroupStore>(),
    app.Services.GetRequiredService<IFigureStore>(),
    app.Services.GetRequiredService<ICommunityStore>(),
    app.Services.GetRequiredService<IPostStore>());

// One-time cleanup on startup: drop any legacy comments from non-approved figures.
try { app.Services.GetRequiredService<IPostService>().PurgeUnapproved(); } catch { }

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program { }

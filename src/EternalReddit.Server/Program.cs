using System.Net.Http.Headers;
using System.Security.Claims;
using EternalReddit.Server.Data;
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

builder.Services.AddAuthorization();

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
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapPostEndpoints();
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

app.MapGet("/api/me", async (HttpContext http, IAuthenticationSchemeProvider schemes) =>
{
    var providers = (await schemes.GetAllSchemesAsync())
        .Where(s => s.Name != "Cookies")
        .Select(s => s.Name)
        .ToArray();
    return Results.Ok(new
    {
        authenticated = http.User.Identity?.IsAuthenticated ?? false,
        name = http.User.Identity?.Name,
        providers
    });
});

app.MapFallbackToFile("index.html");

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program { }

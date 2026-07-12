using System.Net.Http.Headers;
using System.Security.Claims;
using EternalX.Blazor.Server.Data;
using EternalX.Blazor.Server.Endpoints;
using EternalX.Blazor.Server.Hubs;
using EternalX.Blazor.Server.Services;
using EternalX.Blazor.Server.Services.Ai;
using EternalX.Blazor.Server.Services.Moderation;
using EternalX.Blazor.Server.Services.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// --- MVC / hosting ---
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();

// --- Data (LiteDB) ---
builder.Services.AddSingleton<LiteDbContext>();
builder.Services.AddSingleton<IPostStore, LiteDbPostStore>();
builder.Services.AddSingleton<IUserStore, LiteDbUserStore>();
builder.Services.AddSingleton<IModerationLogStore, LiteDbModerationLogStore>();

// --- Core services ---
builder.Services.AddSingleton<IClock, EternalX.Blazor.Server.Services.SystemClock>();
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
    .AddCookie("Cookies");

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
            request.Headers.UserAgent.ParseAdd("EternalX");
            using var response = await ctx.Backchannel.SendAsync(request, ctx.HttpContext.RequestAborted);
            response.EnsureSuccessStatusCode();
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            ctx.RunClaimActions(json.RootElement);
        };
    });
}

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    var store = app.Services.GetRequiredService<IPostStore>();
    if (store.GetRecent(1).Count == 0)
        EternalX.Blazor.Server.DevSeed.Seed(store);

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

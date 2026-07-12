using EternalX.Blazor.Server.Data;
using EternalX.Blazor.Server.Endpoints;
using EternalX.Blazor.Server.Services;
using EternalX.Blazor.Server.Services.Ai;
using EternalX.Blazor.Server.Services.Moderation;
using EternalX.Blazor.Server.Services.RateLimiting;
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
builder.Services.AddSingleton<IClock, SystemClock>();
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
// NOTE: GitHub is OAuth2, not true OIDC (no discovery/id_token). Kept as a
// scaffold; a dedicated OAuth handler is the correct fix (flagged).
AddOidc("GitHub", "https://github.com/login/oauth");

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
app.MapHealthChecks("/health");
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program { }

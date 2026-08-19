using System.Text;
using System.Text.Json.Serialization;
using Cortex.Core.Auth;
using Cortex.Core.Data;
using Cortex.Core.Providers;
using Cortex.Core.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

// ---- EF Core / Postgres ----
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ---- Options ----
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<OAuthOptions>(builder.Configuration.GetSection("OAuth"));
builder.Services.Configure<ProviderOptions>(builder.Configuration.GetSection("Providers"));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection("Cors"));
builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection("Chat"));

// ---- Auth services ----
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IOAuthService, OAuthService>();
builder.Services.AddHttpClient("oauth-google");
builder.Services.AddHttpClient("oauth-github");

// ---- BYOK vault (Data Protection; set DataProtection:KeyRingPath in production) ----
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
var dataProtection = builder.Services.AddDataProtection();
if (!string.IsNullOrEmpty(keyRingPath))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
builder.Services.AddSingleton<ISecretProtector, SecretProtector>();
builder.Services.AddScoped<IProviderKeyStore, ProviderKeyService>();

// ---- Application services ----
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddSingleton<IModelService, ModelService>();

// ---- Providers ----
builder.Services.AddHttpClient(OpenRouterProvider.HttpClientName);
builder.Services.AddHttpClient(OllamaProvider.HttpClientName);
builder.Services.AddHttpClient(LmStudioProvider.HttpClientName);
builder.Services.AddHttpClient(OpenAiProvider.HttpClientName);
builder.Services.AddHttpClient(AnthropicProvider.HttpClientName);
builder.Services.AddHttpClient(GeminiProvider.HttpClientName);
builder.Services.AddHttpClient(XaiProvider.HttpClientName);
builder.Services.AddHttpClient(MistralProvider.HttpClientName);
builder.Services.AddHttpClient(DeepSeekProvider.HttpClientName);
builder.Services.AddSingleton<OpenRouterProvider>();
builder.Services.AddSingleton<OllamaProvider>();
builder.Services.AddSingleton<LmStudioProvider>();
builder.Services.AddSingleton<OpenAiProvider>();
builder.Services.AddSingleton<AnthropicProvider>();
builder.Services.AddSingleton<GeminiProvider>();
builder.Services.AddSingleton<XaiProvider>();
builder.Services.AddSingleton<MistralProvider>();
builder.Services.AddSingleton<DeepSeekProvider>();
builder.Services.AddSingleton<IProviderFactory, ProviderFactory>();

// ---- JWT auth ----
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));

builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// ---- CORS (mobile/web app) ----
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                  ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .SetIsOriginAllowed(host => true)));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok", ts = DateTime.UtcNow }));

app.Run();

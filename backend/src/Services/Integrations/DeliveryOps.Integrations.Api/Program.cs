using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Integrations.Api.Persistence;
using DeliveryOps.Integrations.Api.Security;
using DeliveryOps.Integrations.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using System.Threading.RateLimiting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("JWT issuer is missing.");
string jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("JWT audience is missing.");
IReadOnlyList<SecurityKey> jwtValidationKeys = JwtKeyMaterial.CreateValidationKeys(
    builder.Configuration["Jwt:PublicKeyPem"], builder.Configuration["Jwt:PublicKeyPemBase64"],
    null, null,
    builder.Configuration.GetSection("Jwt:PreviousPublicKeysPem").Get<string[]>() ?? [],
    builder.Configuration.GetSection("Jwt:PreviousPublicKeysPemBase64").Get<string[]>() ?? [],
    builder.Configuration["Jwt:SigningKey"], builder.Environment.IsDevelopment());

builder.Services.AddDbContext<IntegrationsDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("IntegrationsDatabase")
    ?? throw new InvalidOperationException("Connection string 'IntegrationsDatabase' is missing."),
    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "integrations")));
builder.Services.AddHttpClient<CoreOrdersClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["CoreApi:BaseUrl"] ?? "http://localhost:5100/");
    client.DefaultRequestHeaders.Add("X-DeliveryOps-Internal-Key", builder.Configuration["CoreApi:InternalApiKey"] ?? string.Empty);
    client.Timeout = TimeSpan.FromSeconds(15);
});
IDataProtectionBuilder dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("DeliveryOps.Integrations");
string? dataProtectionKeyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyRingPath))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyRingPath));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<IntegrationSecurityOptions>(
    builder.Configuration.GetSection(IntegrationSecurityOptions.SectionName));
int webhookRateLimit = Math.Clamp(
    builder.Configuration.GetValue<int?>("IntegrationSecurity:WebhookRateLimitPerMinute") ?? 300, 10, 10_000);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Webhook istek limiti aşıldı.",
            Detail = "Bağlantı için bir dakika sonra tekrar deneyin."
        }, cancellationToken);
    };
    options.AddPolicy("provider-webhooks", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Request.RouteValues["connectionId"]?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = webhookRateLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddSingleton<WebhookSecretProtector>();
builder.Services.AddScoped<WebhookAuthenticator>();
builder.Services.AddSingleton<IOrderProviderAdapter, CanonicalV1OrderAdapter>();
builder.Services.AddSingleton<IOrderProviderAdapter, YemeksepetiPartnerV2OrderAdapter>();
builder.Services.AddSingleton<IOrderProviderAdapter, GetirFoodV1OrderAdapter>();
builder.Services.AddSingleton<IOrderProviderAdapter, TrendyolWebhookV1OrderAdapter>();
builder.Services.AddSingleton<ProviderAdapterRegistry>();
builder.Services.AddScoped<InboundEventProcessor>();
builder.Services.AddHostedService<InboundEventWorker>();
builder.Services.AddHttpClient("YemeksepetiPartner", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<YemeksepetiPartnerClient>();
builder.Services.AddHttpClient(GetirFoodClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<GetirFoodClient>();
builder.Services.AddHttpClient(TrendyolGoClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<TrendyolGoClient>();
builder.Services.AddScoped<InboundEventIngestor>();
builder.Services.AddHostedService<TrendyolOrderPollingWorker>();
builder.Services.Configure<IntegrationHealthCheckOptions>(
    builder.Configuration.GetSection("IntegrationHealthChecks"));
builder.Services.AddScoped<IntegrationConnectionHealthChecker>();
builder.Services.AddHostedService<IntegrationConnectionHealthWorker>();
builder.Services.Configure<IntegrationRetentionOptions>(
    builder.Configuration.GetSection("IntegrationRetention"));
builder.Services.AddHostedService<IntegrationRetentionWorker>();
builder.Services.AddScoped<OutboundEventProcessor>();
builder.Services.AddHostedService<OutboundEventWorker>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwtIssuer,
        ValidateAudience = true, ValidAudience = jwtAudience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = jwtValidationKeys,
        ValidAlgorithms = jwtValidationKeys.Select(JwtKeyMaterial.AlgorithmFor).Distinct().ToArray(),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
    };
}).AddScheme<AuthenticationSchemeOptions, InternalApiKeyAuthenticationHandler>(
    InternalApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    foreach (string permission in Permissions.All)
        options.AddPolicy(permission, policy => policy.RequireClaim(Permissions.ClaimType, permission));
    options.AddPolicy(InternalPermissions.OrderStatusWrite, policy =>
    {
        policy.AddAuthenticationSchemes(InternalApiKeyAuthenticationHandler.SchemeName);
        policy.RequireClaim(Permissions.ClaimType, InternalPermissions.OrderStatusWrite);
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddHealthChecks().AddDbContextCheck<IntegrationsDbContext>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "DeliveryOps Integrations API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});
builder.Services.AddCors(options => options.AddPolicy("AdminPanel", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));

WebApplication app = builder.Build();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseExceptionHandler();
app.UseCors("AdminPanel");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using IServiceScope scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>().Database.MigrateAsync();
}

app.Run();
public partial class Program;

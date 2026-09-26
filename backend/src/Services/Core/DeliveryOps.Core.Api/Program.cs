using DeliveryOps.Core.Api.Security;
using DeliveryOps.Core.Api.Infrastructure;
using DeliveryOps.Core.Handlers.Businesses;
using DeliveryOps.Core.Infrastructure;
using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using FluentValidation;
using MediatR;
using Serilog;
using DeliveryOps.Core.Api.Realtime;
using DeliveryOps.Core.Queries.Locations;
using DeliveryOps.Core.Queries.Orders;
using DeliveryOps.Core.Api.Notifications;
using DeliveryOps.Core.Api.Dispatch;
using DeliveryOps.Core.Handlers.Dispatch;
using DeliveryOps.Core.Api.Operations;
using DeliveryOps.Core.Queries.Operations;
using DeliveryOps.Core.Queries.Dispatch;
using DeliveryOps.Core.Api.Integrations;
using DeliveryOps.Core.Api.Routing;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;
using System.Threading.RateLimiting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddProblemDetails();
builder.Services.Configure<CoreRetentionOptions>(builder.Configuration.GetSection("CoreRetention"));
builder.Services.AddOptions<IntegrationOutboxOptions>()
    .Bind(builder.Configuration.GetSection("IntegrationOutbox"))
    .Validate(options => options.MaxAttempts is >= 1 and <= 100,
        "IntegrationOutbox:MaxAttempts must be between 1 and 100.")
    .ValidateOnStart();
builder.Services.AddOptions<NotificationOutboxOptions>()
    .Bind(builder.Configuration.GetSection("NotificationOutbox"))
    .Validate(options => options.MaxAttempts is >= 1 and <= 100,
        "NotificationOutbox:MaxAttempts must be between 1 and 100.")
    .ValidateOnStart();
builder.Services.AddHostedService<CoreRetentionWorker>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("order-creation", context => RateLimitPartition.GetSlidingWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 30, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6,
            QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("courier-location", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue("courier_id") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "DeliveryOps Core API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});
builder.Services.AddMediatR(configuration =>
    configuration.RegisterServicesFromAssemblyContaining<CreateBusinessHandler>());
builder.Services.AddValidatorsFromAssemblyContaining<CreateBusinessHandler>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddCoreInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<CoreDbContext>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
builder.Services.AddSignalR();
builder.Services.AddScoped<SignalROperationsNotifier>();
builder.Services.AddScoped<IOperationsNotifier>(provider => provider.GetRequiredService<SignalROperationsNotifier>());
builder.Services.AddScoped<IOrderOperationsNotifier>(provider => provider.GetRequiredService<SignalROperationsNotifier>());
builder.Services.AddScoped<IOperationalAlertNotifier>(provider => provider.GetRequiredService<SignalROperationsNotifier>());
builder.Services.AddHostedService<CourierPresenceMonitor>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<RoadRoutingOptions>()
    .Bind(builder.Configuration.GetSection("RoadRouting"))
    .Validate(options => options.TimeoutSeconds is >= 1 and <= 30,
        "RoadRouting:TimeoutSeconds must be between 1 and 30.")
    .Validate(options => options.MaxElementsPerRequest is >= 1 and <= 625,
        "RoadRouting:MaxElementsPerRequest must be between 1 and 625.")
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.GoogleApiKey),
        "RoadRouting:GoogleApiKey is required when road routing is enabled.")
    .ValidateOnStart();
builder.Services.AddHttpClient<IRoadRouteDistanceProvider, GoogleRoutesRoadDistanceProvider>(client =>
{
    client.BaseAddress = new Uri("https://routes.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("RoadRouting:TimeoutSeconds", 5));
});
builder.Services.AddHttpClient("Notifications", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Notifications:BaseUrl"] ?? "http://localhost:5300/");
    client.DefaultRequestHeaders.Add("X-DeliveryOps-Internal-Key",
        builder.Configuration["Notifications:InternalApiKey"] ?? string.Empty);
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHostedService<NotificationOutboxDispatcher>();
builder.Services.AddHttpClient("Integrations", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Integrations:BaseUrl"] ?? "http://localhost:5400/");
    client.DefaultRequestHeaders.Add("X-DeliveryOps-Internal-Key",
        builder.Configuration["Integrations:InternalApiKey"] ?? string.Empty);
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHostedService<IntegrationOutboxDispatcher>();
builder.Services.AddHostedService<AutomaticDispatchWorker>();
builder.Services.AddHostedService<OperationalAlertWorker>();
builder.Services.AddScoped<DispatchCandidateFinder>();
IReadOnlyList<SecurityKey> jwtValidationKeys = JwtKeyMaterial.CreateValidationKeys(
    builder.Configuration["Jwt:PublicKeyPem"], builder.Configuration["Jwt:PublicKeyPemBase64"],
    null, null,
    builder.Configuration.GetSection("Jwt:PreviousPublicKeysPem").Get<string[]>() ?? [],
    builder.Configuration.GetSection("Jwt:PreviousPublicKeysPemBase64").Get<string[]>() ?? [],
    builder.Configuration["Jwt:SigningKey"], builder.Environment.IsDevelopment());
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = jwtValidationKeys,
            ValidAlgorithms = jwtValidationKeys.Select(JwtKeyMaterial.AlgorithmFor).Distinct().ToArray(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                string? accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/operations"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    })
    .AddScheme<AuthenticationSchemeOptions, InternalApiKeyAuthenticationHandler>(
        InternalApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    foreach (string permission in Permissions.All)
        options.AddPolicy(permission, policy => policy.RequireClaim(Permissions.ClaimType, permission));
    foreach (string permission in new[]
             {
                 InternalPermissions.OrdersIngest,
                 InternalPermissions.ReferencesRead,
                 InternalPermissions.OperationalAlertsWrite
             })
    {
        options.AddPolicy(permission, policy =>
        {
            policy.AddAuthenticationSchemes(InternalApiKeyAuthenticationHandler.SchemeName);
            policy.RequireClaim(Permissions.ClaimType, permission);
        });
    }
});
builder.Services.AddCors(options => options.AddPolicy("AdminPanel", policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseCors("AdminPanel");
app.UseAuthentication();
app.UseMiddleware<CourierIdentityValidationMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapHub<OperationsHub>("/hubs/operations");
app.MapHealthChecks("/health");
app.Run();

public partial class Program;

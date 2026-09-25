using DeliveryOps.Auth.Api.Domain;
using DeliveryOps.Auth.Api.Persistence;
using DeliveryOps.Auth.Api.Security;
using DeliveryOps.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using DeliveryOps.Auth.Api.Infrastructure;
using DeliveryOps.Auth.Api.Operations;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
JwtOptions jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration is missing.");
IReadOnlyList<SecurityKey> jwtValidationKeys = JwtKeyMaterial.CreateValidationKeys(
    jwtOptions.PublicKeyPem, jwtOptions.PublicKeyPemBase64,
    jwtOptions.PrivateKeyPem, jwtOptions.PrivateKeyPemBase64,
    jwtOptions.PreviousPublicKeysPem, jwtOptions.PreviousPublicKeysPemBase64,
    jwtOptions.SigningKey, builder.Environment.IsDevelopment());

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddOptions<AuthRetentionOptions>()
    .Bind(builder.Configuration.GetSection("AuthRetention"))
    .Validate(options => options.ExpiredRefreshTokenDays is >= 1 and <= 365 &&
                         options.ScanIntervalHours is >= 1 and <= 168,
        "AuthRetention values are outside their supported range.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<AuthRetentionWorker>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("AuthDatabase")
    ?? throw new InvalidOperationException("Connection string 'AuthDatabase' is not configured.")));
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.User.RequireUniqueEmail = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddSignInManager();
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtOptions.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = jwtValidationKeys,
        ValidAlgorithms = jwtValidationKeys.Select(JwtKeyMaterial.AlgorithmFor).Distinct().ToArray(),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization(options =>
{
    foreach (string permission in Permissions.All)
        options.AddPolicy(permission, policy => policy.RequireClaim(Permissions.ClaimType, permission));
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AuthExceptionHandler>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("authentication", context => RateLimitPartition.GetSlidingWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("token-refresh", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddControllers();
builder.Services.AddHealthChecks().AddDbContextCheck<AuthDbContext>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "DeliveryOps Auth API", Version = "v1" });
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
builder.Services.AddCors(options => options.AddPolicy("AdminPanel", policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

WebApplication app = builder.Build();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseCors("AdminPanel");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

using (IServiceScope scope = app.Services.CreateScope())
{
    await AuthSeed.InitializeAsync(scope.ServiceProvider, app.Configuration);
}

app.Run();

public partial class Program;

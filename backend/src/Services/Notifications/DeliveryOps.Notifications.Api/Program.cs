using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Notifications.Api.Persistence;
using DeliveryOps.Notifications.Api.Security;
using DeliveryOps.Notifications.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("JWT issuer is missing.");
string jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("JWT audience is missing.");
IReadOnlyList<SecurityKey> jwtValidationKeys = JwtKeyMaterial.CreateValidationKeys(
    builder.Configuration["Jwt:PublicKeyPem"], builder.Configuration["Jwt:PublicKeyPemBase64"],
    null, null,
    builder.Configuration.GetSection("Jwt:PreviousPublicKeysPem").Get<string[]>() ?? [],
    builder.Configuration.GetSection("Jwt:PreviousPublicKeysPemBase64").Get<string[]>() ?? [],
    builder.Configuration["Jwt:SigningKey"], builder.Environment.IsDevelopment());

builder.Services.AddDbContext<NotificationsDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("NotificationsDatabase")
    ?? throw new InvalidOperationException("Connection string 'NotificationsDatabase' is missing."),
    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "notifications")));
builder.Services.AddHttpClient<ExpoPushService>(client =>
{
    client.BaseAddress = new Uri("https://exp.host/");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<ExpoReceiptWorker>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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
    })
    .AddScheme<AuthenticationSchemeOptions, InternalApiKeyAuthenticationHandler>(
        InternalApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(InternalPermissions.NotificationsWrite, policy =>
    {
        policy.AddAuthenticationSchemes(InternalApiKeyAuthenticationHandler.SchemeName);
        policy.RequireClaim(Permissions.ClaimType, InternalPermissions.NotificationsWrite);
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddHealthChecks().AddDbContextCheck<NotificationsDbContext>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "DeliveryOps Notifications API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});
builder.Services.AddCors(options => options.AddPolicy("Clients", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));

WebApplication app = builder.Build();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseExceptionHandler();
app.UseCors("Clients");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using IServiceScope scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
}

app.Run();
public partial class Program;

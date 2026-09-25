using DeliveryOps.Auth.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace DeliveryOps.Auth.Api.Persistence;

public static class AuthSeed
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        AuthDbContext context = services.GetRequiredService<AuthDbContext>();
        await context.Database.OpenConnectionAsync();
        DbConnection connection = context.Database.GetDbConnection();
        await using DbCommand lockCommand = connection.CreateCommand();
        lockCommand.CommandText = "SELECT pg_advisory_lock(73021991)";
        await lockCommand.ExecuteNonQueryAsync();
        try
        {
            if (configuration.GetValue("Database:AutoMigrate", false))
                await context.Database.MigrateAsync();

            RoleManager<IdentityRole<Guid>> roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            foreach (string role in AppRoles.All)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    IdentityResult roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                    if (!roleResult.Succeeded)
                        throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(error => error.Description)));
                }
            }

            string? email = configuration["Seed:PlatformAdmin:Email"];
            string? password = configuration["Seed:PlatformAdmin:Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

            UserManager<ApplicationUser> userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            if (await userManager.FindByEmailAsync(email) is not null) return;

            ApplicationUser admin = new()
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                FirstName = "Platform",
                LastName = "Admin",
                EmailConfirmed = true
            };
            IdentityResult result = await userManager.CreateAsync(admin, password);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
            IdentityResult roleAdded = await userManager.AddToRoleAsync(admin, AppRoles.PlatformAdmin);
            if (!roleAdded.Succeeded)
                throw new InvalidOperationException(string.Join("; ", roleAdded.Errors.Select(x => x.Description)));
        }
        finally
        {
            await using DbCommand unlockCommand = connection.CreateCommand();
            unlockCommand.CommandText = "SELECT pg_advisory_unlock(73021991)";
            await unlockCommand.ExecuteNonQueryAsync();
            await context.Database.CloseConnectionAsync();
        }
    }
}

using BookingApi.Data;
using BookingApi.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BookingApi.Tests.Fixtures;

// The JWT settings the test host runs with. Tests that craft their own tokens sign them with these.
public static class TestJwt
{
    public const string Key = "integration-test-signing-key-that-is-comfortably-longer-than-32-bytes";
    public const string Issuer = "booking-api-integration-tests";
    public const string Audience = "booking-api-integration-tests-clients";
}

// The real application, in memory, wired to the Testcontainers Postgres instead of the dev database.
public class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    static ApiFactory()
    {
        // Environment variables are loaded by WebApplication.CreateBuilder before any line of Program.cs runs,
        // so they win over user-secrets and appsettings files AND are visible to code that reads configuration
        // eagerly while registering services (such as the JWT validation parameters). They also mean the tests
        // never depend on a developer's user-secrets being present.
        Environment.SetEnvironmentVariable("JwtSettings__Key", TestJwt.Key);
        Environment.SetEnvironmentVariable("JwtSettings__Issuer", TestJwt.Issuer);
        Environment.SetEnvironmentVariable("JwtSettings__Audience", TestJwt.Audience);
        Environment.SetEnvironmentVariable("JwtSettings__ExpiryMinutes", "60");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // 1. Point the app at the test container. EF Core registers more than just DbContextOptions<T>;
            //    leaving the options-configuration descriptor behind makes two providers collide at runtime.
            //    It is matched by name so this file does not depend on the type's (internal-ish) namespace.
            var databaseDescriptors = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    (d.ServiceType.IsGenericType
                     && d.ServiceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration")
                     && d.ServiceType.GenericTypeArguments[0] == typeof(AppDbContext)))
                .ToList();
            foreach (var descriptor in databaseDescriptors) services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

            // 2. No expiration job. It sweeps once at startup using the real clock, against a database shared
            //    with every other test class, and could expire rows those tests are relying on.
            var jobDescriptors = services.Where(d =>
                    d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType == typeof(WaitlistExpirationService))
                .ToList();
            foreach (var descriptor in jobDescriptors) services.Remove(descriptor);
        });
    }
}
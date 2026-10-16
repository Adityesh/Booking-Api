using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookingApi.Data;
using BookingApi.Jobs;
using BookingApi.Service;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BookingApi.Extensions;

public static class ConfigureService
{
    extension(IServiceCollection services)
    {
        // The JSON wire format, applied to BOTH option sets: MVC's JsonOptions is what controllers serialize with,
        // while the OpenAPI generator reads the Minimal API (Http.Json) options. Setting only one makes the document
        // disagree with what the API actually sends.
        public void ConfigureJson()
        {
            services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o => ApplyWireFormat(o.JsonSerializerOptions));
            services.ConfigureHttpJsonOptions(o => ApplyWireFormat(o.SerializerOptions));
        }

        public void ConfigureCors()
        {
            services.AddCors(options =>
            {
                options.AddPolicy("AllowFrontend", policy =>
                {
                    policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });
        }

        public void ConfigureAuthentication(IConfiguration configuration)
        {
            services.AddAuthorization();
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = configuration["JwtSettings:Issuer"],
                        ValidAudience = configuration["JwtSettings:Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(configuration["JwtSettings:Key"]!))
                    };
                });
        }

        public void ConfigureDbContext(IConfiguration configuration)
        {
            services.AddDbContext<AppDbContext>(options =>
                options
                    .UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
                    .UseSnakeCaseNamingConvention());
        }

        public void ConfigureScopedService()
        {
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IResourceService, ResourceService>();
            services.AddScoped<IBookingService, BookingService>();
            services.AddScoped<IWaitlistService, WaitlistService>();
            services.AddScoped<IAuditLogService, AuditLogService>();
            services.AddScoped<IReportService, ReportService>();
            services.AddScoped<IAdminService, AdminService>();
        }

        public void ConfigureValidation()
        {
            services.AddValidatorsFromAssemblyContaining<Program>();
        }

        public void ConfigureBackgroundServices()
        {
            services.AddHostedService<WaitlistExpirationService>();
        }
    }

    // Strict: the web defaults accept "42" for an int, so OpenAPI documents every int as integer|string.
    // String enums: "Confirmed", not 0 — readable, and the same names the database already stores.
    private static void ApplyWireFormat(JsonSerializerOptions options)
    {
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new JsonStringEnumConverter());
    }
}

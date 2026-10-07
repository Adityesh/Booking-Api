using System.Text;
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
}
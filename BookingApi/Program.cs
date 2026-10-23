using BookingApi.Dto.Admin;
using BookingApi.Helpers;
using BookingApi.Service;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureDbContext(builder.Configuration);
builder.Services.AddControllers();
builder.Services.ConfigureJson();
builder.Services.AddOpenApi();
builder.Services.ConfigureCors();
builder.Services.ConfigureAuthentication(builder.Configuration);
builder.Services.ConfigureScopedService();
builder.Services.ConfigureValidation();
builder.Services.ConfigureBackgroundServices();
builder.Services.ConfigureAppOptions(builder.Configuration);

var app = builder.Build();

if (args.Length > 0 && args[0].Equals("create-admin", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    void CancelHandler(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        cts.Cancel();
    }

    Console.CancelKeyPress += CancelHandler;

    try
    {
        var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();

        var userName = app.Configuration["CreateAdmin:Username"] ?? "";
        var password = app.Configuration["CreateAdmin:Password"] ?? "";

        var result = await adminService.CreateAdmin(userName, password, cts.Token);

        switch (result)
        {
            case AdminCreationResult.Created:
                Console.WriteLine($"Admin '{userName}' created.");
                Environment.ExitCode = 0;
                break;
            case AdminCreationResult.AdminAlreadyExists:
                // Idempotent: the end state is what the deploy wanted, so this is not a failure.
                Console.WriteLine("An admin already exists. Nothing to do.");
                Environment.ExitCode = 0;
                break;
            case AdminCreationResult.UsernameTaken:
                Console.Error.WriteLine($"Username '{userName}' is already taken by another user.");
                Environment.ExitCode = 1;
                break;
            default:
                Console.Error.WriteLine("Invalid username or password. Check CreateAdmin__Username and CreateAdmin__Password.");
                Environment.ExitCode = 1;
                break;
        }
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Operation aborted by user or timed out.");
        Environment.ExitCode = 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"create-admin failed: {ex.Message}");
        Environment.ExitCode = 1;
    }
    finally
    {
        Console.CancelKeyPress -= CancelHandler;
    }

    return;
}

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var exception = exceptionFeature?.Error;

        var response = new ProblemDetails()
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = app.Environment.IsDevelopment() ? exception?.Message : null,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
        };

        await context.Response.WriteAsJsonAsync(response);
    });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
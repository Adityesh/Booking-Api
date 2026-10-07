using BookingApi.Dto;
using BookingApi.Service;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto, IValidator<LoginDto> validator, CancellationToken token)
    {
        var validationResult = await validator.ValidateAsync(dto, token);
        if (!validationResult.IsValid)
            return ValidationProblem(new ValidationProblemDetails(
                validationResult.ToDictionary()));

        var result = await authService.LoginAsync(dto);
        return result == null ? Unauthorized("Incorrect credentials") : Ok(result);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto, IValidator<RegisterDto> validator, CancellationToken token)
    {
        var validationResult = await validator.ValidateAsync(dto, token);
        if (!validationResult.IsValid)
            return ValidationProblem(new ValidationProblemDetails(
                validationResult.ToDictionary()));

        var result = await authService.RegisterAsync(dto);
        return result == null ? Conflict("Username already taken") : Ok(result);
    }
}
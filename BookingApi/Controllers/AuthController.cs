using BookingApi.Dto;
using BookingApi.Dto.Auth;
using BookingApi.Service;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<string>(StatusCodes.Status401Unauthorized, "text/plain")]
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
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<string>(StatusCodes.Status409Conflict, "text/plain")]
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
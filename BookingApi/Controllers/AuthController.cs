using BookingApi.Dto;
using BookingApi.Dto.Auth;
using BookingApi.Helpers;
using BookingApi.Service;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController(IAuthService authService, IRefreshTokenService refreshTokenService) : ControllerBase
{
    [HttpPost("login")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<string>(StatusCodes.Status401Unauthorized, "text/plain")]
    public async Task<IActionResult> Login(LoginDto dto, IValidator<LoginDto> validator, CancellationToken token)
    {
        var validationResult = await validator.ValidateAsync(dto, token);
        if (!validationResult.IsValid)
            return ValidationProblem(new ValidationProblemDetails(
                validationResult.ToDictionary()));

        var result = await authService.LoginAsync(dto, token);
        if (result == null) return Unauthorized("Incorrect credentials");
        Response.Cookies.Append(CookieConstants.Name, result.RefreshToken, CookieConstants.SetCookieOptions(result.RefreshExpiresAt));
        return Ok(result.Response);
    }

    [HttpPost("register")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<string>(StatusCodes.Status409Conflict, "text/plain")]
    public async Task<IActionResult> Register(RegisterDto dto, IValidator<RegisterDto> validator, CancellationToken token)
    {
        var validationResult = await validator.ValidateAsync(dto, token);
        if (!validationResult.IsValid)
            return ValidationProblem(new ValidationProblemDetails(
                validationResult.ToDictionary()));

        var result = await authService.RegisterAsync(dto, token);
        if (result == null) return Conflict("Username already taken");
        Response.Cookies.Append(CookieConstants.Name, result.RefreshToken, CookieConstants.SetCookieOptions(result.RefreshExpiresAt));
        return Ok(result.Response);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken token)
    {
        if (Request.Cookies.TryGetValue(CookieConstants.Name, out var refreshToken))
        {
            if (!string.IsNullOrEmpty(refreshToken))
            {
                await refreshTokenService.RevokeAsync(refreshToken, token);
            }
        }

        Response.Cookies.Delete(CookieConstants.Name, CookieConstants.DeleteCookie());
        return NoContent();
    }

    [HttpPost("refresh")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<string>(StatusCodes.Status401Unauthorized, "text/plain")]
    public async Task<IActionResult> RefreshToken(CancellationToken token)
    {

        if (!Request.Cookies.TryGetValue(CookieConstants.Name, out var rawToken) || string.IsNullOrEmpty(rawToken))
        {
            return Unauthorized("Session expired");
        }
        var result = await authService.RefreshAsync(rawToken, token);

        if (result == null)
        {
            Response.Cookies.Delete(CookieConstants.Name, CookieConstants.DeleteCookie());
            return Unauthorized("Session expired");
        }

        Response.Cookies.Append(CookieConstants.Name, result.RefreshToken, CookieConstants.SetCookieOptions(result.RefreshExpiresAt));
        return Ok(result.Response);
    }
}
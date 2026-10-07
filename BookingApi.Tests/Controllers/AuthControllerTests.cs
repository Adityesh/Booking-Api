using BookingApi.Controllers;
using BookingApi.Dto;
using BookingApi.Service;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BookingApi.Tests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _authService = new();
    private readonly Mock<IValidator<LoginDto>> _loginValidator = new();
    private readonly Mock<IValidator<RegisterDto>> _registerValidator = new();
    private readonly AuthController _controller;

    private static readonly AuthResponseDto SuccessResponse = new("jwt-token", "alice_01");

    public AuthControllerTests()
    {
        _controller = new AuthController(_authService.Object);
    }

    private static ValidationResult Valid() => new();

    private static ValidationResult Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);

    // ---------- Login ----------

    [Fact]
    public async Task Login_InvalidInput_Returns400_AndNeverCallsService()
    {
        var dto = new LoginDto("", "");
        _loginValidator
            .Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Invalid("Username", "Username cannot be empty"));

        var result = await _controller.Login(dto, _loginValidator.Object, CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains("Username", problem.Errors.Keys);
        _authService.Verify(s => s.LoginAsync(It.IsAny<LoginDto>()), Times.Never);
    }

    [Fact]
    public async Task Login_BadCredentials_Returns401()
    {
        var dto = new LoginDto("alice_01", "wrong_pw");
        _loginValidator
            .Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Valid());
        _authService.Setup(s => s.LoginAsync(It.IsAny<LoginDto>())).ReturnsAsync((AuthResponseDto?)null);

        var result = await _controller.Login(dto, _loginValidator.Object, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTheAuthResponse()
    {
        var dto = new LoginDto("alice_01", "s3cret_pw");
        _loginValidator
            .Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Valid());
        _authService.Setup(s => s.LoginAsync(It.IsAny<LoginDto>())).ReturnsAsync(SuccessResponse);

        var result = await _controller.Login(dto, _loginValidator.Object, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(SuccessResponse, ok.Value);
    }

    // ---------- Register ----------

    [Fact]
    public async Task Register_InvalidInput_Returns400_AndNeverCallsService()
    {
        var dto = new RegisterDto("ab", "short");
        _registerValidator
            .Setup(v => v.ValidateAsync(It.IsAny<RegisterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Invalid("Password", "Password must contain at least one hyphen (-), underscore (_), or period (.)."));

        var result = await _controller.Register(dto, _registerValidator.Object, CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains("Password", problem.Errors.Keys);
        _authService.Verify(s => s.RegisterAsync(It.IsAny<RegisterDto>()), Times.Never);
    }

    [Fact]
    public async Task Register_UsernameTaken_Returns409()
    {
        var dto = new RegisterDto("alice_01", "s3cret_pw");
        _registerValidator
            .Setup(v => v.ValidateAsync(It.IsAny<RegisterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Valid());
        _authService.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>())).ReturnsAsync((AuthResponseDto?)null);

        var result = await _controller.Register(dto, _registerValidator.Object, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Register_ValidInput_Returns200WithTheAuthResponse()
    {
        var dto = new RegisterDto("alice_01", "s3cret_pw");
        _registerValidator
            .Setup(v => v.ValidateAsync(It.IsAny<RegisterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Valid());
        _authService.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>())).ReturnsAsync(SuccessResponse);

        var result = await _controller.Register(dto, _registerValidator.Object, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(SuccessResponse, ok.Value);
    }
}
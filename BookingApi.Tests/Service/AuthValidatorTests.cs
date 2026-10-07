using BookingApi.Dto;
using BookingApi.Dto.Validators;
using FluentValidation.TestHelper;

namespace BookingApi.Tests.Service;

public class AuthValidatorTests
{
    private const string ValidUsername = "alice_01";
    private const string ValidPassword = "s3cret_pw";

    private readonly RegisterDtoValidator _register = new();
    private readonly LoginDtoValidator _login = new();

    // ---------- Register: username (3-30 chars; ASCII letters, digits, _ . - only) ----------

    [Theory]
    [InlineData("abc")]
    [InlineData("alice")]
    [InlineData("a.b_c-d")]
    [InlineData("User123")]
    public void Register_ValidUsername_HasNoUsernameError(string username)
    {
        var result = _register.TestValidate(new RegisterDto(username, ValidPassword));

        result.ShouldNotHaveValidationErrorFor(x => x.Username);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData(" alice")]
    [InlineData("alice ")]
    [InlineData("ali ce")]
    [InlineData("ali@ce")]
    [InlineData("ali!ce")]
    [InlineData("alicé")]
    [InlineData("alice\n")]
    public void Register_InvalidUsername_HasUsernameError(string username)
    {
        var result = _register.TestValidate(new RegisterDto(username, ValidPassword));

        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void Register_UsernameAtMaxLength_IsValid()
    {
        var result = _register.TestValidate(new RegisterDto(new string('a', 30), ValidPassword));

        result.ShouldNotHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void Register_UsernameOverMaxLength_IsInvalid()
    {
        var result = _register.TestValidate(new RegisterDto(new string('a', 31), ValidPassword));

        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    // ---------- Register: password (8-72 chars, must contain at least one of - _ .) ----------

    [Theory]
    [InlineData("abcdefg_")]            // exactly 8 characters
    [InlineData("pass-word")]
    [InlineData("pass.word1")]
    [InlineData("Correct_Horse_Battery")]
    public void Register_ValidPassword_HasNoPasswordError(string password)
    {
        var result = _register.TestValidate(new RegisterDto(ValidUsername, password));

        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short_1")]             // 7 characters
    [InlineData("password123")]         // long enough, no - _ .
    [InlineData("Password 1")]          // a space is not an allowed special character
    public void Register_InvalidPassword_HasPasswordError(string password)
    {
        var result = _register.TestValidate(new RegisterDto(ValidUsername, password));

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Register_PasswordAtMaxLength_IsValid()
    {
        var password = new string('a', 71) + "_";   // 72 characters

        var result = _register.TestValidate(new RegisterDto(ValidUsername, password));

        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Register_PasswordOverMaxLength_IsInvalid()
    {
        var password = new string('a', 72) + "_";   // 73 characters

        var result = _register.TestValidate(new RegisterDto(ValidUsername, password));

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Register_PasswordWithoutSpecialCharacter_ReportsTheSpecialCharacterMessage()
    {
        var result = _register.TestValidate(new RegisterDto(ValidUsername, "password123"));

        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must contain at least one hyphen (-), underscore (_), or period (.).");
    }

    [Fact]
    public void Register_ValidUsernameAndPassword_IsValid()
    {
        var result = _register.TestValidate(new RegisterDto(ValidUsername, ValidPassword));

        result.ShouldNotHaveAnyValidationErrors();
    }

    // ---------- Login: deliberately lenient (only "not empty") ----------

    [Fact]
    public void Login_ValidCredentials_IsValid()
    {
        var result = _login.TestValidate(new LoginDto(ValidUsername, ValidPassword));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Login_EmptyUsername_HasUsernameError(string username)
    {
        var result = _login.TestValidate(new LoginDto(username, ValidPassword));

        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Login_EmptyPassword_HasPasswordError(string password)
    {
        var result = _login.TestValidate(new LoginDto(ValidUsername, password));

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Login_DoesNotApplyTheRegistrationPolicy()
    {
        // A username/password that registration would reject must still reach the service on login,
        // so tightening the registration policy later never locks out accounts created under the old rules.
        var result = _login.TestValidate(new LoginDto("a b", "x"));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
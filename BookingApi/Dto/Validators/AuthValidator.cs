using FluentValidation;

namespace BookingApi.Dto.Validators;

public class RegisterDtoValidator : AbstractValidator<RegisterDto>
{
    public RegisterDtoValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .WithMessage("Username cannot be empty")
            .Length(3, 30)
            .WithMessage("Username must be between 3 and 30 characters")
            .Matches(@"^[A-Za-z0-9_.-]+\z")
            .WithMessage("Username can only contain letters, digits, underscore (_), period (.) and hyphen (-)");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Password cannot be empty")
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters")
            .MaximumLength(72)
            .WithMessage("Password cannot be longer than 72 characters")
            .Matches(@"[-_.]")
            .WithMessage("Password must contain at least one hyphen (-), underscore (_), or period (.).");
    }
}

public class LoginDtoValidator : AbstractValidator<LoginDto>
{
    public LoginDtoValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .WithMessage("Username cannot be empty");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Password cannot be empty");
    }
}
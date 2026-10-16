using FluentValidation;

namespace BookingApi.Dto.Validators;

public class CreateResourceDtoValidator : AbstractValidator<CreateResourceDto>
{
    public CreateResourceDtoValidator()
    {
        // NotEmpty also rejects null and whitespace-only names. The column is unbounded text, so this is the only limit.
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name cannot be empty")
            .MaximumLength(100)
            .WithMessage("Name cannot be longer than 100 characters");

        // Capacity 0 or below would waitlist every request forever: the capacity check is overlapping >= Capacity.
        RuleFor(x => x.Capacity)
            .GreaterThan(0)
            .WithMessage("Capacity must be greater than 0");

        // JsonStringEnumConverter still accepts integers, so "type": 7 binds to an undefined ResourceType.
        RuleFor(x => x.Type)
            .IsInEnum()
            .WithMessage("Type must be Room or Equipment");
    }
}

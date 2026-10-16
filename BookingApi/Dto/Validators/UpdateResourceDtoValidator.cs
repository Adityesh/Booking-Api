using FluentValidation;

namespace BookingApi.Dto.Validators;

// Same rules as CreateResourceDtoValidator: an update must not be able to produce a resource that create would reject.
public class UpdateResourceDtoValidator : AbstractValidator<UpdateResourceDto>
{
    public UpdateResourceDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name cannot be empty")
            .MaximumLength(100)
            .WithMessage("Name cannot be longer than 100 characters");

        RuleFor(x => x.Capacity)
            .GreaterThan(0)
            .WithMessage("Capacity must be greater than 0");

        RuleFor(x => x.Type)
            .IsInEnum()
            .WithMessage("Type must be Room or Equipment");
    }
}

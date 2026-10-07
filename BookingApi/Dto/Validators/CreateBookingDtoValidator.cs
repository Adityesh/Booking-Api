using BookingApi.Dto.Booking;
using FluentValidation;

namespace BookingApi.Dto.Validators;

public class CreateBookingDtoValidator : AbstractValidator<CreateBookingDto>
{
    public CreateBookingDtoValidator()
    {
        RuleFor(b => b.StartTime)
            .GreaterThan(DateTime.UtcNow)
            .WithMessage("Cannot book a time in the past");

        RuleFor(b => b.EndTime)
            .GreaterThan(x => x.StartTime)
            .WithMessage("EndTime must be after StartTime");
    }
}
using BookingApi.Dto.Booking;
using FluentValidation;

namespace BookingApi.Dto.Validators;

public class GetAdminBookingValidator : AbstractValidator<GetAdminBookingDto>
{
    public GetAdminBookingValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page number must be greater than 0");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("Page size must be in between 1 and 100");

        RuleFor(x => x.Status)
            .IsInEnum()
            .WithMessage("Status can be cancelled or confirmed");

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("From cannot be later than To");
    }
}
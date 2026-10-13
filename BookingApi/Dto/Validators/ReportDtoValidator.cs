using BookingApi.Dto.Reporting;
using FluentValidation;

namespace BookingApi.Dto.Validators;

public class ReportDtoValidator : AbstractValidator<ReportGetDto>
{
    public ReportDtoValidator()
    {
        RuleFor(b => b.To)
            .NotEmpty()
            .WithMessage("To Date is required")
            .GreaterThan(x => x.From)
            .WithMessage("To must be after From");

        RuleFor(b => b.From)
            .NotEmpty()
            .WithMessage("From Date is required")
            .Must((dto, x) => (dto.To - x).TotalDays <= 366)
            .WithMessage("Range cannot be more than 366 days");


        RuleFor(b => b.Top)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Top must be greater than 0")
            .LessThanOrEqualTo(50)
            .WithMessage("Top cannot be greater than 50")
            .When(x => x.Top != null);
    }
}
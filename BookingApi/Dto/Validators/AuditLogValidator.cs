using BookingApi.Dto.AuditLog;
using FluentValidation;

namespace BookingApi.Dto.Validators;

public class AuditLogValidator : AbstractValidator<GetAuditLogDto>
{
    public AuditLogValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page number must be greater than 0");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("Page size must be in between 1 and 100");

        RuleFor(x => x.UserId)
            .Null()
            .When(x => x.SystemOnly)
            .WithMessage("System Only must be false when user id is valid");

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("From cannot be later than To");
    }
}
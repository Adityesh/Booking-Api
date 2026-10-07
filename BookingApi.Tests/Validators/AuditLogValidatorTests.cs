using BookingApi.Data;
using BookingApi.Dto.AuditLog;
using BookingApi.Dto.Validators;
using FluentValidation.TestHelper;

namespace BookingApi.Tests.Validators;

public class AuditLogValidatorTests
{
    private readonly AuditLogValidator _validator = new();

    private static readonly DateTimeOffset T = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static GetAuditLogDto Dto(
        bool systemOnly = false, int? userId = null, ActionType? action = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, int page = 1, int pageSize = 20) =>
        new(userId, action, from, to, systemOnly, page, pageSize);

    // ---------- the baseline: every filter is optional ----------

    [Fact]
    public void NoFilters_DefaultPaging_IsValid()
    {
        var result = _validator.TestValidate(Dto());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EveryFilterSuppliedAtOnce_ExceptTheConflictingOnes_IsValid()
    {
        var result = _validator.TestValidate(
            Dto(userId: 7, action: ActionType.BookingCreated, from: T, to: T.AddDays(30), page: 3, pageSize: 50));

        result.ShouldNotHaveAnyValidationErrors();
    }

    // ---------- paging ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1000)]
    public void Page_OneOrMore_IsValid(int page)
    {
        _validator.TestValidate(Dto(page: page)).ShouldNotHaveValidationErrorFor(x => x.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Page_BelowOne_IsInvalid(int page)
    {
        _validator.TestValidate(Dto(page: page)).ShouldHaveValidationErrorFor(x => x.Page);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(100)]
    public void PageSize_BetweenOneAndOneHundred_IsValid(int pageSize)
    {
        _validator.TestValidate(Dto(pageSize: pageSize)).ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    [InlineData(1000)]
    public void PageSize_OutsideOneToOneHundred_IsInvalid(int pageSize)
    {
        _validator.TestValidate(Dto(pageSize: pageSize)).ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    // ---------- system-only vs user id ----------

    [Fact]
    public void SystemOnly_TogetherWithAUserId_IsInvalid()
    {
        // "Entries with no user" and "entries by user 7" can never both be true.
        var result = _validator.TestValidate(Dto(systemOnly: true, userId: 7));

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void SystemOnly_WithoutAUserId_IsValid()
    {
        _validator.TestValidate(Dto(systemOnly: true)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void AUserId_WithoutSystemOnly_IsValid()
    {
        _validator.TestValidate(Dto(userId: 7)).ShouldNotHaveAnyValidationErrors();
    }

    // ---------- date range ----------

    [Fact]
    public void From_LaterThanTo_IsInvalid()
    {
        var result = _validator.TestValidate(Dto(from: T.AddDays(1), to: T));

        result.ShouldHaveValidationErrorFor(x => x.To);
    }

    [Fact]
    public void From_EqualToTo_IsValid()
    {
        // An empty half-open range: legal, it just matches nothing.
        _validator.TestValidate(Dto(from: T, to: T)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void From_EarlierThanTo_IsValid()
    {
        _validator.TestValidate(Dto(from: T, to: T.AddDays(1))).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void OnlyFrom_IsValid()
    {
        _validator.TestValidate(Dto(from: T)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void OnlyTo_IsValid()
    {
        // The comparison must be skipped when there is nothing to compare against.
        _validator.TestValidate(Dto(to: T)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void SameInstantInDifferentTimeZones_IsValid()
    {
        // Equal instants, different offsets: compared as instants, not as wall-clock text.
        var result = _validator.TestValidate(Dto(from: T, to: T.ToOffset(TimeSpan.FromHours(5.5))));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void FromLaterThanTo_AcrossTimeZones_IsInvalid()
    {
        // 02:00 at +05:30 is 20:30 UTC the previous day, which is earlier than T (00:00 UTC), so from > to.
        var from = new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.FromHours(5.5)).AddDays(1);
        var result = _validator.TestValidate(Dto(from: from, to: T));

        result.ShouldHaveValidationErrorFor(x => x.To);
    }
}
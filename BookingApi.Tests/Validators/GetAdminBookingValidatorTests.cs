using BookingApi.Data;
using BookingApi.Dto.Booking;
using BookingApi.Dto.Validators;
using FluentValidation.TestHelper;

namespace BookingApi.Tests.Validators;

public class GetAdminBookingValidatorTests
{
    private readonly GetAdminBookingValidator _validator = new();

    private static readonly DateTimeOffset T = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static GetAdminBookingDto Dto(
        int? userId = null, int? resourceId = null, BookingStatus? status = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, int page = 1, int pageSize = 20) =>
        new(UserId: userId, ResourceId: resourceId, Status: status, From: from, To: to, Page: page, PageSize: pageSize);

    [Fact]
    public void NoFilters_DefaultPaging_IsValid()
    {
        _validator.TestValidate(Dto()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EveryFilterAtOnce_IsValid()
    {
        var result = _validator.TestValidate(Dto(userId: 7, resourceId: 3, status: BookingStatus.Cancelled,
            from: T, to: T.AddDays(30), page: 3, pageSize: 100));

        result.ShouldNotHaveAnyValidationErrors();
    }

    // ---------- paging ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Page_BelowOne_IsInvalid(int page)
    {
        _validator.TestValidate(Dto(page: page)).ShouldHaveValidationErrorFor(x => x.Page);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void PageSize_AtTheBounds_IsValid(int pageSize)
    {
        _validator.TestValidate(Dto(pageSize: pageSize)).ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public void PageSize_OutsideOneToOneHundred_IsInvalid(int pageSize)
    {
        _validator.TestValidate(Dto(pageSize: pageSize)).ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    // ---------- status ----------

    [Fact]
    public void Status_NotAnEnumMember_IsInvalid()
    {
        // ?status=5 binds to (BookingStatus)5; only IsInEnum stops it.
        _validator.TestValidate(Dto(status: (BookingStatus)5)).ShouldHaveValidationErrorFor(x => x.Status);
    }

    // ---------- date range ----------

    [Fact]
    public void From_LaterThanTo_IsInvalid()
    {
        _validator.TestValidate(Dto(from: T.AddDays(1), to: T)).ShouldHaveValidationErrorFor(x => x.To);
    }

    [Fact]
    public void From_EqualToTo_IsValid()
    {
        // An empty half-open range: legal, it just matches nothing (same rule as the audit log).
        _validator.TestValidate(Dto(from: T, to: T)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void OnlyFrom_OrOnlyTo_IsValid()
    {
        _validator.TestValidate(Dto(from: T)).ShouldNotHaveAnyValidationErrors();
        _validator.TestValidate(Dto(to: T)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void SameInstantInDifferentTimeZones_IsValid()
    {
        _validator.TestValidate(Dto(from: T, to: T.ToOffset(TimeSpan.FromHours(5.5)))).ShouldNotHaveAnyValidationErrors();
    }
}

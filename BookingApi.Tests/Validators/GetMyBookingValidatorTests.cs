using BookingApi.Data;
using BookingApi.Dto.Booking;
using BookingApi.Dto.Validators;
using FluentValidation.TestHelper;

namespace BookingApi.Tests.Validators;

public class GetMyBookingValidatorTests
{
    private readonly GetMyBookingValidator _validator = new();

    private static GetMyBookingDto Dto(
        BookingStatus? status = null, BookingTimeFrame? when = null, int page = 1, int pageSize = 20) =>
        new(Status: status, When: when, Page: page, PageSize: pageSize);

    [Theory]
    [InlineData(null)]
    [InlineData(BookingTimeFrame.Upcoming)]
    [InlineData(BookingTimeFrame.Past)]
    public void EveryTimeFrame_IncludingNone_IsValid(BookingTimeFrame? when)
    {
        _validator.TestValidate(Dto(when: when)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void When_NotAnEnumMember_IsInvalid()
    {
        _validator.TestValidate(Dto(when: (BookingTimeFrame)7)).ShouldHaveValidationErrorFor(x => x.When);
    }

    [Fact]
    public void Status_NotAnEnumMember_IsInvalid()
    {
        _validator.TestValidate(Dto(status: (BookingStatus)5)).ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Page_BelowOne_IsInvalid(int page)
    {
        _validator.TestValidate(Dto(page: page)).ShouldHaveValidationErrorFor(x => x.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void PageSize_OutsideOneToOneHundred_IsInvalid(int pageSize)
    {
        _validator.TestValidate(Dto(pageSize: pageSize)).ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void PageSize_AtTheBounds_IsValid(int pageSize)
    {
        _validator.TestValidate(Dto(pageSize: pageSize)).ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }
}

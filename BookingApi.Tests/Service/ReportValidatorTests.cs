using BookingApi.Dto.Reporting;
using BookingApi.Dto.Validators;

namespace BookingApi.Tests.Validators;

public class ReportDtoValidatorTests
{
    private readonly ReportDtoValidator _validator = new();

    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ReportGetDto Dto(DateTimeOffset? from = null, DateTimeOffset? to = null, int? top = null) =>
        new(From: from ?? From, To: to ?? From.AddDays(30), Top: top);

    [Fact]
    public void ValidRequest_WithoutTop_IsValid()
    {
        var result = _validator.Validate(Dto());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void MissingTo_IsRejected_WithTheRequiredMessage()
    {
        var result = _validator.Validate(new ReportGetDto(From: From, To: default, Top: null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "To" && e.ErrorMessage == "To Date is required");
    }

    [Fact]
    public void MissingFrom_IsRejected_WithTheRequiredMessage()
    {
        var result = _validator.Validate(new ReportGetDto(From: default, To: From, Top: null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "From" && e.ErrorMessage == "From Date is required");
    }

    [Fact]
    public void BothDatesMissing_ReportsBothAsRequired()
    {
        var result = _validator.Validate(new ReportGetDto(From: default, To: default, Top: null));

        Assert.Contains(result.Errors, e => e.ErrorMessage == "To Date is required");
        Assert.Contains(result.Errors, e => e.ErrorMessage == "From Date is required");
    }

    [Fact]
    public void ToEqualToFrom_IsRejected()
    {
        var result = _validator.Validate(Dto(to: From));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "To" && e.ErrorMessage == "To must be after From");
    }

    [Fact]
    public void ToBeforeFrom_IsRejected()
    {
        var result = _validator.Validate(Dto(to: From.AddDays(-1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "To" && e.ErrorMessage == "To must be after From");
    }

    [Fact]
    public void RangeOfExactly366Days_IsValid()
    {
        var result = _validator.Validate(Dto(to: From.AddDays(366)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RangeOf366DaysAndAnHour_IsRejected()
    {
        // TimeSpan.Days would truncate this to 366 and let it through; TotalDays does not.
        var result = _validator.Validate(Dto(to: From.AddDays(366).AddHours(1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.PropertyName == "From" && e.ErrorMessage == "Range cannot be more than 366 days");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    public void Top_InsideOneToFifty_IsValid(int top)
    {
        var result = _validator.Validate(Dto(top: top));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0, "Top must be greater than 0")]
    [InlineData(-5, "Top must be greater than 0")]
    [InlineData(51, "Top cannot be greater than 50")]
    [InlineData(1000, "Top cannot be greater than 50")]
    public void Top_OutsideOneToFifty_IsRejected(int top, string message)
    {
        var result = _validator.Validate(Dto(top: top));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Top" && e.ErrorMessage == message);
    }

    [Fact]
    public void Top_Null_IsValid_BecauseTheServiceDefaultsItToTen()
    {
        var result = _validator.Validate(Dto(top: null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DatesWithDifferentOffsets_AreComparedAsInstants_NotAsClockTimes()
    {
        // "05:30 at +05:30" has a later clock time than From (00:00), but it is the SAME instant (00:00 UTC),
        // so To is not after From and must be rejected.
        var to = new DateTimeOffset(2026, 1, 1, 5, 30, 0, TimeSpan.FromHours(5.5));

        var result = _validator.Validate(Dto(to: to));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "To must be after From");
    }
}
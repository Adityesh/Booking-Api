using BookingApi.Controllers;
using BookingApi.Dto.Reporting;
using BookingApi.Service;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BookingApi.Tests.Controllers;

// Branching logic only: validation failure => 400 and the service is never called; success => 200 with the service
// result. These tests cannot prove [Authorize] or [FromQuery] binding; ReportsEndpointTests does that.
public class ReportsControllerTests
{
    private readonly Mock<IReportService> _service = new();
    private readonly Mock<IValidator<ReportGetDto>> _validator = new();
    private readonly ReportsController _controller;

    private static readonly ReportGetDto Dto = new(
        From: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        To: new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero),
        Top: 5);

    public ReportsControllerTests()
    {
        _controller = new ReportsController(_service.Object);
    }

    private void ValidatorReturnsValid() =>
        _validator.Setup(v => v.ValidateAsync(It.IsAny<ReportGetDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

    private void ValidatorReturnsInvalid() =>
        _validator.Setup(v => v.ValidateAsync(It.IsAny<ReportGetDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("To", "To must be after From")]));

    private static void AssertValidationProblem(IActionResult result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
        var details = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains("To", details.Errors.Keys);
    }

    // ---------- most-booked-resources ----------

    [Fact]
    public async Task MostBookedResources_ValidRequest_ReturnsOkWithTheServiceResult()
    {
        ValidatorReturnsValid();
        var expected = new List<MostBookedResourceDto> { new("Room A", 1, 3.5, 4, true) };
        _service.Setup(s => s.MostBookedResources(Dto, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await _controller.MostBookedResources(Dto, _validator.Object, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        _service.Verify(s => s.MostBookedResources(Dto, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MostBookedResources_InvalidRequest_Returns400_AndNeverCallsTheService()
    {
        ValidatorReturnsInvalid();

        var result = await _controller.MostBookedResources(Dto, _validator.Object, CancellationToken.None);

        AssertValidationProblem(result);
        _service.Verify(
            s => s.MostBookedResources(It.IsAny<ReportGetDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- average-booking-duration ----------

    [Fact]
    public async Task AverageBookingDuration_ValidRequest_ReturnsOkWithTheServiceResult()
    {
        ValidatorReturnsValid();
        var expected = new AverageBookingDurationDto(12, 74.33m);
        _service.Setup(s => s.AverageBookingDuration(Dto, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await _controller.AverageBookingDuration(Dto, _validator.Object, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        _service.Verify(s => s.AverageBookingDuration(Dto, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AverageBookingDuration_NoBookings_StillReturns200_WithTheEmptyShape()
    {
        // "No data" is a normal answer, not an error and not a 404.
        ValidatorReturnsValid();
        var empty = new AverageBookingDurationDto(0, null);
        _service.Setup(s => s.AverageBookingDuration(Dto, It.IsAny<CancellationToken>())).ReturnsAsync(empty);

        var result = await _controller.AverageBookingDuration(Dto, _validator.Object, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AverageBookingDurationDto>(ok.Value);
        Assert.Equal(0, body.BookingCount);
        Assert.Null(body.AverageMinutes);
    }

    [Fact]
    public async Task AverageBookingDuration_InvalidRequest_Returns400_AndNeverCallsTheService()
    {
        ValidatorReturnsInvalid();

        var result = await _controller.AverageBookingDuration(Dto, _validator.Object, CancellationToken.None);

        AssertValidationProblem(result);
        _service.Verify(
            s => s.AverageBookingDuration(It.IsAny<ReportGetDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- bookings-per-day ----------

    [Fact]
    public async Task BookingsPerDay_ValidRequest_ReturnsOkWithTheServiceResult()
    {
        ValidatorReturnsValid();
        var expected = new List<BookingsPerDayDto> { new(new DateOnly(2026, 1, 1), 3), new(new DateOnly(2026, 1, 2), 0) };
        _service.Setup(s => s.BookingsPerDay(Dto, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await _controller.BookingsPerDay(Dto, _validator.Object, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        _service.Verify(s => s.BookingsPerDay(Dto, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BookingsPerDay_InvalidRequest_Returns400_AndNeverCallsTheService()
    {
        ValidatorReturnsInvalid();

        var result = await _controller.BookingsPerDay(Dto, _validator.Object, CancellationToken.None);

        AssertValidationProblem(result);
        _service.Verify(
            s => s.BookingsPerDay(It.IsAny<ReportGetDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
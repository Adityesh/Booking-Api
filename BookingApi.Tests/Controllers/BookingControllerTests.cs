using BookingApi.Controllers;
using BookingApi.Dto.Booking;
using BookingApi.Service;
using BookingApi.Tests.Helpers;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BookingApi.Tests.Controllers;

public class BookingControllerWaitlistTests
{
    private readonly Mock<IBookingService> _mockBookingService = new();
    private readonly BookingController _controller;

    public BookingControllerWaitlistTests()
    {
        _controller = new BookingController(_mockBookingService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = AuthHelper.CreateUser("User", 1) }
            }
        };
    }

    private static CreateBookingDto ValidDto() =>
        new(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1), 1);

    // Mocks the real interface member, IValidator<T>.ValidateAsync(T, CancellationToken).
    private static Mock<IValidator<CreateBookingDto>> ValidatorReturning(ValidationResult result)
    {
        var mock = new Mock<IValidator<CreateBookingDto>>();
        mock.Setup(v => v.ValidateAsync(It.IsAny<CreateBookingDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    private void ServiceReturns(BookingCreationResult outcome) =>
        _mockBookingService
            .Setup(s => s.CreateAsync(It.IsAny<CreateBookingDto>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((outcome, null));

    [Fact]
    public async Task Create_WaitListed_ReturnsAcceptedWithBody_AndNoLocationHeader()
    {
        // Guards against Accepted("some message"), which binds to the (string uri) overload:
        // the message would land in the Location header and the body would be empty.
        ServiceReturns(BookingCreationResult.WaitListed);

        var result = await _controller.Create(ValidDto(), ValidatorReturning(new ValidationResult()).Object,
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Null(accepted.Location);
        Assert.NotNull(accepted.Value);
    }

    [Fact]
    public async Task Create_AlreadyWaitListed_ReturnsConflict()
    {
        ServiceReturns(BookingCreationResult.AlreadyWaitListed);

        var result = await _controller.Create(ValidDto(), ValidatorReturning(new ValidationResult()).Object,
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Create_ValidationFails_Returns400_AndNeverCallsTheService()
    {
        var failures = new List<ValidationFailure> { new("StartTime", "Cannot book a time in the past.") };

        var result = await _controller.Create(ValidDto(), ValidatorReturning(new ValidationResult(failures)).Object,
            CancellationToken.None);

        var problem = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
        _mockBookingService.Verify(
            s => s.CreateAsync(It.IsAny<CreateBookingDto>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
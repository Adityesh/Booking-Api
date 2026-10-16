using BookingApi.Controllers;
using BookingApi.Data;
using BookingApi.Dto.Booking;
using BookingApi.Dto.Waitlist;
using BookingApi.Service;
using BookingApi.Tests.Helpers;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BookingApi.Tests.Controllers;

public class BookingControllerTests
{
    private const int UserId = 1;

    private readonly Mock<IBookingService> _mockBookingService = new();
    private readonly BookingController _controller;

    public BookingControllerTests()
    {
        _controller = ControllerFor("User");
    }

    private BookingController ControllerFor(string role) =>
        new(_mockBookingService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = AuthHelper.CreateUser(role, UserId) }
            }
        };

    private static CreateBookingDto ValidDto() =>
        new(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1), 1);

    private static BookingResponseDto SampleBooking(int id = 42) =>
        new(id, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1), 1, BookingStatus.Confirmed, UserId);

    private static WaitlistEntryResponseDto SampleWaitlistEntry() =>
        new(Id: 7, ResourceId: 1, RequestedStartTime: DateTime.UtcNow.AddDays(1),
            RequestedEndTime: DateTime.UtcNow.AddDays(1).AddHours(1), Status: WaitlistStatus.Waiting,
            CreatedAt: DateTime.UtcNow);

    // Mocks the real interface member, IValidator<T>.ValidateAsync(T, CancellationToken).
    private static Mock<IValidator<CreateBookingDto>> ValidatorReturning(ValidationResult result)
    {
        var mock = new Mock<IValidator<CreateBookingDto>>();
        mock.Setup(v => v.ValidateAsync(It.IsAny<CreateBookingDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    private static IValidator<CreateBookingDto> PassingValidator() => ValidatorReturning(new ValidationResult()).Object;

    private void CreateReturns(BookingCreationOutcome outcome) =>
        _mockBookingService
            .Setup(s => s.CreateAsync(It.IsAny<CreateBookingDto>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

    private void CancelReturns(BookingCancellationResult result) =>
        _mockBookingService
            .Setup(s => s.CancelAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    // ======================= Create =======================

    [Fact]
    public async Task Create_Success_Returns201_PointingAtGetById_WithTheBooking()
    {
        var booking = SampleBooking(id: 42);
        CreateReturns(new BookingCreationOutcome(BookingCreationResult.Success, Booking: booking));

        var result = await _controller.Create(ValidDto(), PassingValidator(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(BookingController.GetById), created.ActionName);
        Assert.Null(created.ControllerName);   // same controller
        Assert.Equal(42, created.RouteValues!["id"]);
        Assert.Same(booking, created.Value);
    }

    [Fact]
    public async Task Create_WaitListed_Returns202_WithTheWaitlistEntry_PointingAtMyWaitlist()
    {
        var entry = SampleWaitlistEntry();
        CreateReturns(new BookingCreationOutcome(BookingCreationResult.WaitListed, WaitlistEntry: entry));

        var result = await _controller.Create(ValidDto(), PassingValidator(), CancellationToken.None);

        var accepted = Assert.IsType<AcceptedAtActionResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        Assert.Equal(nameof(WaitlistController.GetMine), accepted.ActionName);
        Assert.Equal("Waitlist", accepted.ControllerName);
        Assert.Null(accepted.RouteValues);
        // The body is the entry itself, not a message wrapper: the client needs its id to withdraw.
        Assert.Same(entry, accepted.Value);
    }

    [Fact]
    public async Task Create_AlreadyWaitListed_Returns409WithMessage()
    {
        CreateReturns(new BookingCreationOutcome(BookingCreationResult.AlreadyWaitListed));

        var result = await _controller.Create(ValidDto(), PassingValidator(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.IsType<string>(conflict.Value);
    }

    [Theory]
    [InlineData(BookingCreationResult.ResourceNotFound)]
    [InlineData(BookingCreationResult.ResourceInactive)]   // 404, not 403: never reveal that an inactive resource exists
    public async Task Create_ResourceNotFoundOrInactive_ReturnsTheSame404(BookingCreationResult outcome)
    {
        CreateReturns(new BookingCreationOutcome(outcome));

        var result = await _controller.Create(ValidDto(), PassingValidator(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Resource not found.", notFound.Value);
    }

    [Fact]
    public async Task Create_PassesTheUserIdFromTheToken_ToTheService()
    {
        CreateReturns(new BookingCreationOutcome(BookingCreationResult.Success, Booking: SampleBooking()));

        await _controller.Create(ValidDto(), PassingValidator(), CancellationToken.None);

        _mockBookingService.Verify(
            s => s.CreateAsync(It.IsAny<CreateBookingDto>(), UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_ValidationFails_Returns400_AndNeverCallsTheService()
    {
        var failures = new List<ValidationFailure> { new("StartTime", "Cannot book a time in the past.") };

        var result = await _controller.Create(ValidDto(), ValidatorReturning(new ValidationResult(failures)).Object,
            CancellationToken.None);

        var problem = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
        var details = Assert.IsType<ValidationProblemDetails>(problem.Value);
        Assert.Contains("StartTime", details.Errors.Keys);
        _mockBookingService.Verify(
            s => s.CreateAsync(It.IsAny<CreateBookingDto>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ======================= GetById =======================

    [Fact]
    public async Task GetById_Found_Returns200WithTheBooking()
    {
        var booking = SampleBooking(id: 5);
        _mockBookingService
            .Setup(s => s.GetByIdAsync(5, false, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(booking);

        var result = await _controller.GetById(5, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(booking, ok.Value);
    }

    [Fact]
    public async Task GetById_NotFoundOrNotYours_Returns404()
    {
        // The service folds "not yours" into null, so the controller cannot tell them apart, by design.
        _mockBookingService
            .Setup(s => s.GetByIdAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingResponseDto?)null);

        var result = await _controller.GetById(5, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("User", false)]
    [InlineData("Admin", true)]
    public async Task GetById_PassesTheRoleFromTheToken_ToTheService(string role, bool expectedIsAdmin)
    {
        var controller = ControllerFor(role);
        _mockBookingService
            .Setup(s => s.GetByIdAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleBooking());

        await controller.GetById(5, CancellationToken.None);

        _mockBookingService.Verify(s => s.GetByIdAsync(5, expectedIsAdmin, UserId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ======================= Cancel =======================

    [Theory]
    [InlineData(BookingCancellationResult.Success)]
    [InlineData(BookingCancellationResult.AlreadyCancelled)]   // idempotent: a retried cancel lands in the same state
    public async Task Cancel_SuccessOrAlreadyCancelled_Returns204(BookingCancellationResult outcome)
    {
        CancelReturns(outcome);

        var result = await _controller.Cancel(5, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Cancel_NotFoundOrNotYours_Returns404()
    {
        CancelReturns(BookingCancellationResult.NotFound);

        var result = await _controller.Cancel(5, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Cancel_TooCloseToStartTime_Returns409WithMessage()
    {
        CancelReturns(BookingCancellationResult.TooCloseToStartTime);

        var result = await _controller.Cancel(5, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.IsType<string>(conflict.Value);
    }

    [Theory]
    [InlineData("User", false)]
    [InlineData("Admin", true)]   // the admin bypass of the 2-hour rule depends on this flag reaching the service
    public async Task Cancel_PassesUserIdAndRoleFromTheToken_ToTheService(string role, bool expectedIsAdmin)
    {
        var controller = ControllerFor(role);
        CancelReturns(BookingCancellationResult.Success);

        await controller.Cancel(5, CancellationToken.None);

        _mockBookingService.Verify(s => s.CancelAsync(5, UserId, expectedIsAdmin, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

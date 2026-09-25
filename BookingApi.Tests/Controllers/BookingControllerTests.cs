using BookingApi.Controllers;
using BookingApi.Data;
using BookingApi.Dto.Booking;
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
    private readonly Mock<IBookingService> _mockBookingService;
    private readonly BookingController _controller;

    public BookingControllerTests()
    {
        _mockBookingService = new Mock<IBookingService>();
        _controller = new BookingController(_mockBookingService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = AuthHelper.CreateUser("Admin", 1) }
            }
        };
    }

    [Fact]
    public async Task Create_NoCapacity_ReturnsConflict()
    {
        var mockValidator = new Mock<IValidator<CreateBookingDto>>();
        mockValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CreateBookingDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _mockBookingService
            .Setup(s => s.CreateAsync(It.IsAny<CreateBookingDto>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingCreationResult.NoCapacity, null));

        var result = await _controller.Create(new CreateBookingDto(DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 1), mockValidator.Object, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Create_Success_ReturnsCreatedAtAction()
    {

        var responseDto = new BookingResponseDto(1, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 1,
            BookingStatus.Confirmed, 1);

        var mockValidator = new Mock<IValidator<CreateBookingDto>>();
        mockValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CreateBookingDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _mockBookingService
            .Setup(s => s.CreateAsync(It.IsAny<CreateBookingDto>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingCreationResult.Success, responseDto));

        var result = await _controller.Create(new CreateBookingDto(DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 1), mockValidator.Object, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task Cancel_TooCloseToStartTime_ReturnsConflict()
    {
        _mockBookingService
            .Setup(s => s.CancelAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookingCancellationResult.TooCloseToStartTime);

        var result = await _controller.Cancel(1, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_ReturnsNoContent()
    {
        _mockBookingService
            .Setup(s => s.CancelAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookingCancellationResult.AlreadyCancelled);

        var result = await _controller.Cancel(1, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }





}
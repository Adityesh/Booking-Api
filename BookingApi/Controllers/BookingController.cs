using BookingApi.Dto.Booking;
using BookingApi.Extensions;
using BookingApi.Service;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingController(IBookingService bookingService) : ControllerBase
    {
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create(CreateBookingDto dto, IValidator<CreateBookingDto> validator, CancellationToken token = default)
        {
            var validationResult = await validator.ValidateAsync(dto, token);
            if (!validationResult.IsValid)
                return ValidationProblem(new ValidationProblemDetails(
                    validationResult.ToDictionary()));

            var userId = User.GetUserId();
            var (result, booking) = await bookingService.CreateAsync(dto, userId, token);

            return result switch
            {
                BookingCreationResult.Success => CreatedAtAction(nameof(GetById), new { id = booking!.Id }, booking),
                BookingCreationResult.NoCapacity => Conflict("This resource is fully booked for the requested time."),
                BookingCreationResult.ResourceInactive => NotFound("Resource not found."),
                BookingCreationResult.ResourceNotFound => NotFound("Resource not found."),
                _ => Problem()
            };
        }

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetById(int id, CancellationToken token = default)
        {
            var userId = User.GetUserId();
            var isAdmin = User.IsInRole("Admin");
            var booking = await bookingService.GetByIdAsync(id, isAdmin, userId, token);
            if (booking == null) return NotFound("Booking not found");
            return Ok(booking);
        }

        [HttpPost("{id:int}/cancel")]
        [Authorize]
        public async Task<IActionResult> Cancel(int id, CancellationToken token = default)
        {
            var userId = User.GetUserId();
            var isAdmin = User.IsInRole("Admin");

            var result = await bookingService.CancelAsync(id, userId, isAdmin, token);

            return result switch
            {
                BookingCancellationResult.AlreadyCancelled => NoContent(),
                BookingCancellationResult.NotFound => NotFound("Booking not found"),
                BookingCancellationResult.Success => NoContent(),
                BookingCancellationResult.TooCloseToStartTime => Conflict(
                    "Bookings can only be cancelled upto 2 hours before the start time."),
                _ => Problem()
            };
        }
    }
}

using BookingApi.Dto.Booking;
using BookingApi.Dto.Waitlist;
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
        [ProducesResponseType<BookingResponseDto>(StatusCodes.Status201Created, "application/json")]
        [ProducesResponseType<WaitlistEntryResponseDto>(StatusCodes.Status202Accepted, "application/json")]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
        [ProducesResponseType<string>(StatusCodes.Status404NotFound, "text/plain")]
        [ProducesResponseType<string>(StatusCodes.Status409Conflict, "text/plain")]
        public async Task<IActionResult> Create(CreateBookingDto dto, IValidator<CreateBookingDto> validator, CancellationToken token = default)
        {
            var validationResult = await validator.ValidateAsync(dto, token);
            if (!validationResult.IsValid)
                return ValidationProblem(new ValidationProblemDetails(
                    validationResult.ToDictionary()));

            var userId = User.GetUserId();
            var (result, booking, waitlistEntry) = await bookingService.CreateAsync(dto, userId, token);

            return result switch
            {
                BookingCreationResult.Success => CreatedAtAction(nameof(GetById), new { id = booking!.Id }, booking),
                BookingCreationResult.AlreadyWaitListed => Conflict("User is already in the waitlist."),
                BookingCreationResult.ResourceInactive => NotFound("Resource not found."),
                BookingCreationResult.ResourceNotFound => NotFound("Resource not found."),
                BookingCreationResult.WaitListed => AcceptedAtAction(
                    nameof(WaitlistController.GetMine), "Waitlist", routeValues: null, value: waitlistEntry),
                _ => Problem()
            };
        }

        [HttpGet("{id:int}")]
        [Authorize]
        [ProducesResponseType<BookingResponseDto>(StatusCodes.Status200OK, "application/json")]
        [ProducesResponseType<string>(StatusCodes.Status404NotFound, "text/plain")]
        public async Task<ActionResult<BookingResponseDto>> GetById(int id, CancellationToken token = default)
        {
            var userId = User.GetUserId();
            var isAdmin = User.IsInRole("Admin");
            var booking = await bookingService.GetByIdAsync(id, isAdmin, userId, token);
            if (booking == null) return NotFound("Booking not found");
            return Ok(booking);
        }

        [HttpPost("{id:int}/cancel")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType<string>(StatusCodes.Status404NotFound, "text/plain")]
        [ProducesResponseType<string>(StatusCodes.Status409Conflict, "text/plain")]
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

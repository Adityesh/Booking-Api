using BookingApi.Dto.Waitlist;

namespace BookingApi.Dto.Booking;

public record BookingCreationOutcome(BookingCreationResult Result, BookingResponseDto? Booking = null, WaitlistEntryResponseDto? WaitlistEntry = null);
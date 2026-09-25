namespace BookingApi.Dto.Booking;

public enum BookingCancellationResult
{
    Success,
    NotFound,
    TooCloseToStartTime,
    AlreadyCancelled
}
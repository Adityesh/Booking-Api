namespace BookingApi.Dto.Booking;

public enum BookingCreationResult
{
    Success,
    ResourceNotFound,
    ResourceInactive,
    NoCapacity
}
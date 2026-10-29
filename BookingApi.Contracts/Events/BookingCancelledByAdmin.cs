namespace BookingApi.Contracts.Events;

// Someone other than the owner (only an admin can do that) cancelled a booking.
// Not published when owners cancel their own booking. All times are UTC.
public record BookingCancelledByAdmin(
    int UserId,              // the booking's OWNER: the one to notify, not the admin
    int BookingId,
    int ResourceId,
    string ResourceName,
    DateTime StartTime,
    DateTime EndTime,
    DateTime OccurredAt);
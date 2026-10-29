namespace BookingApi.Contracts.Events;

public record WaitlistEntryPromoted(
    int UserId,
    int WaitlistEntryId,
    int BookingId,
    int ResourceId,
    string ResourceName,
    DateTime StartTime,
    DateTime EndTime,
    DateTime OccurredAt
);
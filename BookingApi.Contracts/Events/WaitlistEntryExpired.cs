namespace BookingApi.Contracts.Events;

// The requested start time passed while the entry was still waiting. Published by the system sweep.
// All times are UTC.
public record WaitlistEntryExpired(
    int UserId,
    int WaitlistEntryId,
    int ResourceId,
    string ResourceName,
    DateTime RequestedStartTime,
    DateTime RequestedEndTime,
    DateTime OccurredAt);
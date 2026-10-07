using BookingApi.Data;

namespace BookingApi.Dto.Waitlist;

public record WaitlistEntryResponseDto(int Id, int ResourceId, DateTime RequestedStartTime, DateTime RequestedEndTime, WaitlistStatus Status, DateTime CreatedAt);
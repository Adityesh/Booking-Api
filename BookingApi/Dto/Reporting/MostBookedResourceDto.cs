namespace BookingApi.Dto.Reporting;

public record MostBookedResourceDto(string Name, int ResourceId, double TotalBookedHours, int BookingCount, bool IsActive);
namespace BookingApi.Dto.Reporting;

public record AverageBookingDurationDto(int BookingCount, decimal? AverageMinutes);
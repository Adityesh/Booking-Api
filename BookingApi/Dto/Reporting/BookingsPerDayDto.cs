namespace BookingApi.Dto.Reporting;

public record BookingsPerDayDto(DateOnly Date, int Count);
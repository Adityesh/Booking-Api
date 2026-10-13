namespace BookingApi.Dto.Reporting;

public record ReportGetDto(DateTimeOffset From, DateTimeOffset To, int? Top = 10);
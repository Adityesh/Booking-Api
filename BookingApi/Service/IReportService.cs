using BookingApi.Dto;
using BookingApi.Dto.Reporting;

namespace BookingApi.Service;

public interface IReportService
{
    public Task<List<MostBookedResourceDto>> MostBookedResources(ReportGetDto dto, CancellationToken token);
    public Task<AverageBookingDurationDto> AverageBookingDuration(ReportGetDto dto, CancellationToken token);
    public Task<List<BookingsPerDayDto>> BookingsPerDay(ReportGetDto dto, CancellationToken token);
}
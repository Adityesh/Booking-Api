using BookingApi.Data;
using BookingApi.Dto.Reporting;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Service;

public class ReportService(AppDbContext context) : IReportService
{
    public async Task<List<MostBookedResourceDto>> MostBookedResources(ReportGetDto dto, CancellationToken token)
    {
        return await context.Bookings
            .Where(x => (x.StartTime >= dto.From.UtcDateTime && x.StartTime < dto.To.UtcDateTime) && x.Status == BookingStatus.Confirmed)
            .AsNoTracking()
            .GroupBy(x => new { x.ResourceId, x.Resource.Name, x.Resource.IsActive })
            .Select(x => new
            {
                x.Key.Name,
                x.Key.ResourceId,
                x.Key.IsActive,
                TotalBookedHours = x.Sum(b => (b.EndTime - b.StartTime).TotalHours),
                BookingCount = x.Count()
            })
            .OrderByDescending(x => x.BookingCount)
            .ThenByDescending(x => x.TotalBookedHours)
            .ThenBy(x => x.ResourceId)
            .Take(dto.Top ?? 10)
            .Select(x => new MostBookedResourceDto(x.Name, x.ResourceId, Math.Round(x.TotalBookedHours, 2), x.BookingCount, x.IsActive))
            .ToListAsync(token);
    }

    public async Task<AverageBookingDurationDto> AverageBookingDuration(ReportGetDto dto, CancellationToken token)
    {
        var result = await context.Bookings
            .Where(x => x.StartTime >= dto.From.UtcDateTime && x.StartTime < dto.To.UtcDateTime &&
                        x.Status == BookingStatus.Confirmed)
            .GroupBy(x => 1)
            .Select(x => new AverageBookingDurationDto(x.Count(), Math.Round(x.Average(b => (decimal)(b.EndTime - b.StartTime).TotalMinutes), 2)))
            .FirstOrDefaultAsync(token);

        return result ?? new AverageBookingDurationDto(0, null);
    }

    public async Task<List<BookingsPerDayDto>> BookingsPerDay(ReportGetDto dto, CancellationToken token)
    {
        var result = await context.Bookings
            .Where(x => x.StartTime >= dto.From.UtcDateTime && x.StartTime < dto.To.UtcDateTime &&
                        x.Status == BookingStatus.Confirmed)
            .GroupBy(x => DateOnly.FromDateTime(x.StartTime))
            .Select(x => new
            {
                Date = x.Key,
                Count = x.Count()
            })
            .ToDictionaryAsync(x => x.Date, x => x.Count, token);

        var startDate = DateOnly.FromDateTime(dto.From.UtcDateTime);
        var endDate = DateOnly.FromDateTime(dto.To.UtcDateTime.AddTicks(-1));

        var dates = Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
            .Select(day => startDate.AddDays(day))
            .ToArray();

        return dates.Select(date => new BookingsPerDayDto(date, result.GetValueOrDefault(date))).ToList();
    }
}
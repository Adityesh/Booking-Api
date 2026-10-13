using BookingApi.Dto.Reporting;
using BookingApi.Service;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Controllers;

[Route("/api/[controller]")]
[ApiController]
[Authorize(Roles="Admin")]
public class ReportsController(IReportService reportService) : ControllerBase
{
    [HttpGet("most-booked-resources")]
    public async Task<IActionResult> MostBookedResources([FromQuery] ReportGetDto dto, IValidator<ReportGetDto> validator, CancellationToken token = default)
    {
        var validationResult = await validator.ValidateAsync(dto, token);
            if (!validationResult.IsValid)
                return ValidationProblem(new ValidationProblemDetails(
                    validationResult.ToDictionary()));

        var result = await reportService.MostBookedResources(dto, token);
        return Ok(result);
    }

    [HttpGet("average-booking-duration")]
    public async Task<IActionResult> AverageBookingDuration([FromQuery] ReportGetDto dto, IValidator<ReportGetDto> validator, CancellationToken token = default)
    {
        var validationResult = await validator.ValidateAsync(dto, token);
            if (!validationResult.IsValid)
                return ValidationProblem(new ValidationProblemDetails(
                    validationResult.ToDictionary()));

        var result = await reportService.AverageBookingDuration(dto, token);
        return Ok(result);
    }

    [HttpGet("bookings-per-day")]
    public async Task<IActionResult> BookingsPerDay([FromQuery] ReportGetDto dto, IValidator<ReportGetDto> validator, CancellationToken token = default)
    {
        var validationResult = await validator.ValidateAsync(dto, token);
            if (!validationResult.IsValid)
                return ValidationProblem(new ValidationProblemDetails(
                    validationResult.ToDictionary()));

        var result = await reportService.BookingsPerDay(dto, token);
        return Ok(result);
    }
}
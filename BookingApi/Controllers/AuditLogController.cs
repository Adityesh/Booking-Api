using BookingApi.Dto;
using BookingApi.Dto.AuditLog;
using BookingApi.Service;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuditLogController(IAuditLogService auditLogService) : ControllerBase
    {
        [HttpGet]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType<PagedResult<AuditLogResponseDto>>(StatusCodes.Status200OK, "application/json")]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
        public async Task<IActionResult> GetAuditLogs([FromQuery] GetAuditLogDto dto,
            IValidator<GetAuditLogDto> validator, CancellationToken token = default)

        {
            var validationResult = await validator.ValidateAsync(dto, token);
            if (!validationResult.IsValid)
                return ValidationProblem(new ValidationProblemDetails(
                    validationResult.ToDictionary()));
            var response = await auditLogService.GetAuditLogsAsync(dto, token);
            return Ok(response);
        }
    }
}

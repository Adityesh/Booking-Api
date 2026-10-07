using BookingApi.Dto;
using BookingApi.Dto.AuditLog;

namespace BookingApi.Service;

public interface IAuditLogService
{
    public void CreateLog(CreateAuditLogDto dto);
    public Task<PagedResult<AuditLogResponseDto>> GetAuditLogsAsync(GetAuditLogDto dto, CancellationToken token);
}
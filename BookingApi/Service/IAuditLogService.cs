using BookingApi.Dto.AuditLog;

namespace BookingApi.Service;

public interface IAuditLogService
{
    public void CreateLog(CreateAuditLogDto dto);
}
using BookingApi.Data;
using BookingApi.Dto.AuditLog;

namespace BookingApi.Service;

public class AuditLogService(AppDbContext context) : IAuditLogService
{
    public void CreateLog(CreateAuditLogDto dto)
    {
        context.AuditLogEntries.Add(new AuditLogEntryEntity()
        {
            Timestamp = DateTime.UtcNow,
            Action = dto.Action,
            EntityId = dto.EntityId,
            UserId = dto.UserId
        });
    }
}
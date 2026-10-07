using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Dto.AuditLog;
using Microsoft.EntityFrameworkCore;

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

    public async Task<PagedResult<AuditLogResponseDto>> GetAuditLogsAsync(GetAuditLogDto dto, CancellationToken token)
    {
        var baseQuery = context.AuditLogEntries
            .AsNoTracking()
            .AsQueryable();

        if (dto.Action.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Action == dto.Action);
        }

        if (dto.From.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Timestamp >= dto.From.Value.UtcDateTime);
        }

        if (dto.To.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Timestamp < dto.To.Value.UtcDateTime);
        }

        if (dto.SystemOnly)
        {
            baseQuery = baseQuery.Where(x => x.UserId == null);
        }

        if (dto.UserId.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.UserId == dto.UserId);
        }

        var totalCount = await baseQuery.CountAsync(token);

        baseQuery = baseQuery
            .OrderByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.Id)
            .Skip((dto.Page - 1) * dto.PageSize)
            .Take(dto.PageSize);

        var data = await baseQuery.Select(x => new AuditLogResponseDto(x.Id, x.EntityId, x.UserId, x.Timestamp, x.Action))
            .ToListAsync(token);

        return new PagedResult<AuditLogResponseDto>(dto.Page, dto.PageSize, totalCount, data);
    }
}
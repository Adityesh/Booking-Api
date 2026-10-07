using BookingApi.Data;

namespace BookingApi.Dto.AuditLog;

public record GetAuditLogDto(
    int? UserId,
    ActionType? Action,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool SystemOnly,
    int Page = 1,
    int PageSize = 20);
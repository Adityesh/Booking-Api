using BookingApi.Data;

namespace BookingApi.Dto.AuditLog;

public record CreateAuditLogDto(int EntityId, int? UserId, ActionType Action);
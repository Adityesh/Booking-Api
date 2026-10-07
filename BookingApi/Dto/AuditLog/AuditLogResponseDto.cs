using BookingApi.Data;

namespace BookingApi.Dto.AuditLog;

public record AuditLogResponseDto(int Id, int EntityId, int? UserId, DateTime Timestamp, ActionType Action);
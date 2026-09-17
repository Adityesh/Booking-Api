namespace BookingApi.Data;

public class AuditLogEntryEntity
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int EntityId { get; set; }
    public ActionType Action { get; set; }

    public int UserId { get; set; }
    public UserEntity User { get; set; } = null!;
}

public enum ActionType
{
    BookingCreated,
    BookingCancelled,
    WaitlistEntryCreated,
    WaitlistEntryPromoted,
    WaitlistEntryExpired,
    ResourceCreated,
    ResourceUpdated,
    ResourceDeleted
}

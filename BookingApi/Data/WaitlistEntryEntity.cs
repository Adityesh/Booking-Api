namespace BookingApi.Data;

public class WaitlistEntryEntity
{
    public int Id { get; set; }
    public DateTime RequestedStartTime { get; set; }
    public DateTime RequestedEndTime { get; set; }
    public WaitlistStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    public int ResourceId { get; set; }
    public ResourceEntity Resource { get; set; } = null!;
    public int UserId { get; set; }
    public UserEntity User { get; set; } = null!;
}

public enum WaitlistStatus
{
    Waiting,
    Promoted,
    Expired
}
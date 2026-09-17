namespace BookingApi.Data;

public class BookingEntity
{
    public int Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public BookingStatus Status { get; set; }

    public int ResourceId { get; set; }
    public ResourceEntity Resource { get; set; } = null!;
    public int UserId { get; set; }
    public UserEntity User { get; set; } = null!;
}

public enum BookingStatus
{
    Confirmed,
    Cancelled
}
namespace BookingApi.Data;

public class ResourceEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ResourceType Type { get; set; }
    public int Capacity { get; set; }
    public bool IsActive { get; set; } = true;

    public List<BookingEntity> Bookings { get; set; } = [];
}

public enum ResourceType
{
    Room,
    Equipment
}
namespace BookingApi.Data;

public class UserEntity
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;

    public List<BookingEntity> Bookings { get; set; } = [];
}

public enum UserRole
{
    Admin,
    User
}
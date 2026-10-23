namespace BookingApi.Data;

public class RefreshTokenEntity
{
    public int Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Guid FamilyId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime AbsoluteExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public RefreshTokenRevokedReason? RevokedReason { get; set; }
    public int? ReplacedById { get; set; }
    public int UserId { get; set; }
    public UserEntity User { get; set; } = null!;
}

public enum RefreshTokenRevokedReason
{
    Rotated,
    Logout,
    ReuseDetected,
}
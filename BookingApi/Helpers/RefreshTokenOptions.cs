namespace BookingApi.Helpers;

public class RefreshTokenOptions
{
    public int ReuseGraceSeconds { get; set; }
    public int AbsoluteLifetimeDays { get; set; }
    public int LifetimeDays { get; set; }
}
namespace BookingApi.Helpers;

public static class CookieConstants
{
    public const string Name = "refresh-token";

    public static CookieOptions SetCookieOptions(DateTime expiresAt)
    {
        return new CookieOptions()
        {
            SameSite = SameSiteMode.Strict,
            Expires = expiresAt,
            HttpOnly = true,
            Path = "/api/Auth",
            Secure = true,
            IsEssential = true,
        };
    }

    public static CookieOptions DeleteCookie()
    {
        return new CookieOptions()
        {
            SameSite = SameSiteMode.Strict,
            HttpOnly = true,
            Path = "/api/Auth",
            Secure = true,
            IsEssential = true,
        };
    }
}
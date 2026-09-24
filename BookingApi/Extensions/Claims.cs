using System.Security.Claims;

namespace BookingApi.Extensions;

public static class Claims
{
    extension(ClaimsPrincipal user)
    {
        public int GetUserId()
        {
            return int.Parse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
        }

        public bool IsInRole(string role)
        {
            var userRole = user.FindFirst(ClaimTypes.Role)?.Value;
            return userRole == role;
        }
    }
}
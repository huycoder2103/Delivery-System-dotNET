using System.Security.Claims;

namespace Delivery_System.Helpers
{
    public static class UserExtensions
    {
        public static string GetUserId(this ClaimsPrincipal user) =>
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        public static string GetFullName(this ClaimsPrincipal user) =>
            user.Identity?.Name ?? "Người dùng";

        public static string GetRole(this ClaimsPrincipal user) =>
            user.FindFirst(ClaimTypes.Role)?.Value ?? "";

        public static int? GetStationId(this ClaimsPrincipal user)
        {
            var sid = user.FindFirst("StationID")?.Value;
            return int.TryParse(sid, out int id) ? id : null;
        }
        
        public static string GetUsername(this ClaimsPrincipal user) =>
            user.FindFirst("Username")?.Value ?? "";
    }
}

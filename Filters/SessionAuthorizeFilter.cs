using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace Delivery_System.Filters
{
    public class SessionAuthorizeFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var controllerName = context.RouteData.Values["controller"]?.ToString();
            var actionName = context.RouteData.Values["action"]?.ToString();

            // 1. NGOẠI LỆ: Không kiểm tra session cho trang Login
            if (controllerName == "Account" && actionName == "Login")
            {
                return;
            }

            // 2. KIỂM TRA AUTHENTICATION (COOKIE)
            var user = context.HttpContext.User;
            if (user == null || !user.Identity.IsAuthenticated)
            {
                // Nếu chưa đăng nhập qua Cookie, đá về trang Login
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            // 3. KIỂM TRA & TÁI NẠP SESSION (Nếu Session bị timeout nhưng Cookie vẫn còn)
            var userId = context.HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId))
            {
                // Lấy lại thông tin từ Claims đã lưu trong Cookie
                var claimUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var claimFullName = user.FindFirst(ClaimTypes.Name)?.Value;
                var claimRole = user.FindFirst(ClaimTypes.Role)?.Value;

                if (!string.IsNullOrEmpty(claimUserId))
                {
                    context.HttpContext.Session.SetString("UserID", claimUserId);
                    context.HttpContext.Session.SetString("FullName", claimFullName ?? "Người dùng");
                    context.HttpContext.Session.SetString("Role", claimRole ?? "");
                }
                else
                {
                    // Trường hợp hy hữu Cookie hợp lệ nhưng thiếu Claim quan trọng
                    context.Result = new RedirectToActionResult("Login", "Account", null);
                }
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            // Không cần xử lý sau khi action chạy
        }
    }
}

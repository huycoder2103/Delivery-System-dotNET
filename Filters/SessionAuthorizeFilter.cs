using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

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

            // 2. KIỂM TRA SESSION
            var userId = context.HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId))
            {
                // Nếu chưa đăng nhập, đá về trang Login ngay lập tức
                context.Result = new RedirectToActionResult("Login", "Account", null);
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            // Không cần xử lý sau khi action chạy
        }
    }
}

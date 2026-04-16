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

            // 1. NGOẠI LỆ: Không kiểm tra cho trang Login
            if (controllerName == "Account" && actionName == "Login")
            {
                return;
            }

            // 2. KIỂM TRA AUTHENTICATION (COOKIE)
            var user = context.HttpContext.User;
            if (user == null || user.Identity?.IsAuthenticated != true)
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}

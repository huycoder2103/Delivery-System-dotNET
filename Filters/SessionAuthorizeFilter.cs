using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Delivery_System.Filters
{
    public class SessionAuthorizeFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            // 0. KIỂM TRA NGOẠI LỆ [AllowAnonymous]
            var endpoint = context.HttpContext.GetEndpoint();
            if (endpoint?.Metadata?.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() != null)
            {
                return;
            }

            var controllerName = context.RouteData.Values["controller"]?.ToString();
            var actionName = context.RouteData.Values["action"]?.ToString();

            // 1. NGOẠI LỆ: Cho phép truy cập trang Login và các trang công khai mà không cần Login
            var path = context.HttpContext.Request.Path.Value?.ToLower() ?? "";
            if (path.Contains("/account/login") || 
                (string.Equals(controllerName, "Account", StringComparison.OrdinalIgnoreCase) && 
                 string.Equals(actionName, "Login", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (string.Equals(controllerName, "Home", StringComparison.OrdinalIgnoreCase) && 
                (string.Equals(actionName, "Error", StringComparison.OrdinalIgnoreCase) || 
                 string.Equals(actionName, "Privacy", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            // 2. KIỂM TRA AUTHENTICATION (COOKIE)
            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                // Nếu chưa login, đẩy về trang Login
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}

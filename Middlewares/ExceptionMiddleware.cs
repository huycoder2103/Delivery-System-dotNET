using System.Net;
using System.Text.Json;

namespace Delivery_System.Middlewares
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IWebHostEnvironment _env;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IWebHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                // 1. Ghi log lỗi vào Console/File (Trong thực tế nên dùng Serilog)
                _logger.LogError(ex, "Một lỗi không mong muốn đã xảy ra: {Message}", ex.Message);

                // 2. Kiểm tra nếu là yêu cầu AJAX hoặc API
                bool isAjax = context.Request.Headers["X-Requested-With"] == "XMLHttpRequest" || 
                              context.Request.ContentType?.Contains("application/json") == true;

                if (isAjax)
                {
                    await HandleAjaxExceptionAsync(context, ex);
                }
                else
                {
                    // 3. Nếu là yêu cầu trang web thông thường, chuyển hướng về trang Error
                    context.Response.Redirect("/Home/Error");
                }
            }
        }

        private static Task HandleAjaxExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new
            {
                success = false,
                message = "Đã có lỗi hệ thống xảy ra. Vui lòng thử lại sau!",
                detail = exception.Message // Chỉ nên hiện detail ở môi trường Development
            };

            return context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}

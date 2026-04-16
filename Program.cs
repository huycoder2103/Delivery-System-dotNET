using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using FluentValidation.AspNetCore;
using Delivery_System.Hubs;
using Delivery_System.Validators;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// 1. TỐI ƯU NETWORK: Thêm dịch vụ nén phản hồi (Response Compression)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Optimal;
});

// 2. TỐI ƯU BACKEND: Thêm Memory Caching & Output Caching
builder.Services.AddMemoryCache();
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("ReportCache", b => b.Expire(TimeSpan.FromSeconds(60)).SetVaryByQuery("reportDate"));
    options.AddPolicy("TripCache", b => b.Expire(TimeSpan.FromSeconds(30)).SetVaryByQuery("page", "departureFilter", "destinationFilter"));
});

builder.Services.AddSignalR();

// 4. BẢO MẬT TẬP TRUNG: Thêm Filter kiểm tra Login toàn cục
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<Delivery_System.Filters.SessionAuthorizeFilter>();
})
.AddFluentValidation(fv => fv.RegisterValidatorsFromAssemblyContaining<OrderValidator>());

// Đăng ký kết nối Database MySQL với DbContext Pooling để tối ưu hóa hiệu suất
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "";

builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), 
        mySqlOptions => {
            mySqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        }));

// Cấu hình Cookie Authentication
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(7); // Duy trì đăng nhập trong 7 ngày
        options.SlidingExpiration = true; // Tự động gia hạn khi người dùng có hoạt động
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.Name = ".DeliverySystem.Auth";
    });

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Sử dụng Middleware xử lý lỗi tập trung ngay đầu Pipeline
app.UseMiddleware<Delivery_System.Middlewares.ExceptionMiddleware>();

// 1. TỐI ƯU NETWORK: Sử dụng nén phản hồi
app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    // app.UseExceptionHandler("/Home/Error"); // Có thể tắt cái này vì đã có Middleware trên
    app.UseHsts();
}

app.UseHttpsRedirection();

// 3. TỐI ƯU NETWORK: Cấu hình Browser Caching cho file tĩnh (CSS, JS, Images) trong 1 năm
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        const int durationInSeconds = 60 * 60 * 24 * 365; // 1 năm
        ctx.Context.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.CacheControl] =
            "public,max-age=" + durationInSeconds;
    }
});

app.UseRouting();
app.UseOutputCache();

app.UseAuthentication(); 
app.UseAuthorization();

app.MapHub<DeliveryHub>("/deliveryHub");
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();

app.Run();

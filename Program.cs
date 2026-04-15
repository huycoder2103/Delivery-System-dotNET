using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);

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

// 4. BẢO MẬT TẬP TRUNG: Thêm Filter kiểm tra Login toàn cục
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<Delivery_System.Filters.SessionAuthorizeFilter>();
});

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

// Kích hoạt Session (Vẫn giữ lại nếu bạn muốn dùng cho các mục đích khác)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// 1. TỐI ƯU NETWORK: Sử dụng nén phản hồi
app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
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

app.UseAuthentication(); // Thêm dòng này trước UseAuthorization
app.UseAuthorization();
app.UseSession();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();

app.Run();

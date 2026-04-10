using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình Nén phản hồi (Response Compression)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

// Add services to the container.
builder.Services.AddControllersWithViews();

// 2. Kích hoạt WebOptimizer (Gộp và nén file tĩnh)
builder.Services.AddWebOptimizer(pipeline =>
{
    pipeline.MinifyCssFiles(); // Nén mọi file .css
    pipeline.MinifyJsFiles();  // Nén mọi file .js
    
    // Gộp các file CSS chính thành 1 bundle duy nhất
    pipeline.AddCssBundle("/css/site.bundle.css", "css/common_styles.css", "css/navbar.css", "css/footer.css");
});

// Đăng ký kết nối Database MySQL với Connection Pooling cấp độ DbContext
var connectionString = "server=delivery-db-mysql-jayker03212k5-ee32.f.aivencloud.com;port=19281;database=defaultdb;user=avnadmin;password=AVNS_txLmskApkmP4v1bHS0y;SslMode=Required;Pooling=true;MinPoolSize=5;MaxPoolSize=100;";
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// Kích hoạt Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Session hết hạn sau 30 phút
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Đăng ký HttpContextAccessor để dùng được Session trong Layout/Views
builder.Services.AddHttpContextAccessor();

// Kích hoạt Memory Cache
builder.Services.AddMemoryCache();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// Kích hoạt nén và gộp file tĩnh (WebOptimizer)
app.UseWebOptimizer();

// Kích hoạt nén phản hồi (Response Compression)
app.UseResponseCompression();

app.UseAuthorization();
app.UseSession(); // BẮT BUỘC phải có dòng này để dùng Session

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        const int durationInSeconds = 60 * 60 * 24 * 365; // 1 năm
        ctx.Context.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.CacheControl] =
            "public,max-age=" + durationInSeconds;
    }
});

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();

app.Run();

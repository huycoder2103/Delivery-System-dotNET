using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using FluentValidation.AspNetCore;
using FluentValidation;
using Delivery_System.Hubs;
using Delivery_System.Validators;
using Serilog;
using System.Security.Cryptography;
using System.Text;

try 
{
    // 1. Khởi tạo Serilog
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File("Logs/startup-log-.txt", rollingInterval: RollingInterval.Day)
        .CreateLogger();

    Log.Information(">>> BẮT ĐẦU KHỞI ĐỘNG HỆ THỐNG (.NET 10)");
    
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // 2. Cấu hình Services
    builder.Services.AddResponseCompression(options => {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
    });

    builder.Services.AddMemoryCache();
    builder.Services.AddSignalR();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddHealthChecks(); // Thêm HealthChecks cho Docker/Cloud

    builder.Services.AddControllersWithViews(options => {
        options.Filters.Add<Delivery_System.Filters.SessionAuthorizeFilter>();
    });

    builder.Services.AddFluentValidationAutoValidation()
                    .AddFluentValidationClientsideAdapters()
                    .AddValidatorsFromAssemblyContaining<OrderValidator>();

    // 3. Cấu hình Database
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "";
    Log.Information(">>> Đang cấu hình Database...");

    builder.Services.AddDbContextPool<AppDbContext>(options => {
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), 
            mySqlOptions => mySqlOptions.EnableRetryOnFailure());
    });

    builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options => {
            options.LoginPath = "/Account/Login";
            options.Cookie.Name = ".DeliverySystem.Auth";
        });

    builder.Services.AddScoped<Delivery_System.Services.IOrderService, Delivery_System.Services.OrderService>();

    var app = builder.Build();

    // 4. Khởi tạo Database
    using (var scope = app.Services.CreateScope()) {
        try {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Log.Information(">>> Đang kiểm tra Database...");
            if (context.Database.EnsureCreated()) {
                Log.Information(">>> Đã tạo mới Database và nạp dữ liệu mẫu...");
                if (!context.TblRoles.Any()) {
                    context.TblRoles.AddRange(
                        new TblRole { RoleId = "AD", RoleName = "Quản trị viên" },
                        new TblRole { RoleId = "US", RoleName = "Nhân viên" }
                    );
                }
                if (!context.TblUsers.Any()) {
                    context.TblUsers.Add(new TblUser {
                        UserId = "AD01", Username = "admin", 
                        Password = "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918", 
                        FullName = "Admin Hệ Thống", RoleId = "AD", Status = true, CreatedAt = DateTime.Now
                    });
                }
                context.SaveChanges();
            }
        } catch (Exception ex) {
            Log.Error(">>> [LỖI DATABASE] {Msg}", ex.Message);
            Log.Warning(">>> Vui lòng kiểm tra lại MySQL (Mật khẩu/Port).");
        }
    }

    // 5. Cấu hình Pipeline
    app.UseMiddleware<Delivery_System.Middlewares.ExceptionMiddleware>();
    
    var supportedCultures = new[] { "vi-VN" };
    app.UseRequestLocalization(new RequestLocalizationOptions()
        .SetDefaultCulture(supportedCultures[0])
        .AddSupportedCultures(supportedCultures)
        .AddSupportedUICultures(supportedCultures));

    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication(); 
    app.UseAuthorization();

    app.MapControllerRoute(name: "default", pattern: "{controller=Account}/{action=Login}/{id?}");
    app.MapHub<DeliveryHub>("/deliveryHub");
    app.MapHealthChecks("/health"); // API kiểm tra trạng thái sống của App
    app.MapStaticAssets(); // Tối ưu tải file tĩnh theo chuẩn .NET 9/10

    Log.Information(">>> HỆ THỐNG ĐÃ SẴN SÀNG!");
    Log.Information(">>> TRUY CẬP: http://localhost:5122");
    
    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine("\n>>> [LỖI KHỞI ĐỘNG]");
    Console.WriteLine(ex.Message);
    if (Log.Logger != null) Log.Fatal(ex, "Start-up failed");
}
finally {
    Log.CloseAndFlush();
}

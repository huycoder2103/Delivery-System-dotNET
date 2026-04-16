using Microsoft.AspNetCore.Mvc;
using Delivery_System.Models;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Delivery_System.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;

        public AccountController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password, bool rememberMe = true)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ViewBag.ErrorMessage = "Vui lòng nhập đầy đủ thông tin!";
                return View();
            }

            // Chuẩn hóa dữ liệu: Cắt khoảng trắng đầu/cuối
            username = username.Trim();
            password = password.Trim();

            string hashedPassword = HashSha256(password);
            var user = await _context.TblUsers
                .FirstOrDefaultAsync(u => u.Username == username && u.Password == hashedPassword && u.Status == true);

            if (user != null)
            {
                // Lấy tên trạm một cách an toàn
                string stationName = "";
                if (user.StationId.HasValue)
                {
                    var station = await _context.TblStations.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.StationId == user.StationId);
                    stationName = station?.StationName ?? "";
                }

                // 1. Tạo danh sách Claims (Thông tin người dùng sẽ lưu trong Cookie)
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.UserId ?? ""),
                    new Claim(ClaimTypes.Name, user.FullName ?? "Người dùng"),
                    new Claim(ClaimTypes.Role, user.RoleId ?? ""),
                    new Claim("Username", user.Username ?? ""),
                    new Claim("Email", user.Email ?? ""),
                    new Claim("StationID", user.StationId?.ToString() ?? ""),
                    new Claim("StationName", stationName) // Lưu tên trạm vào Cookie
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = rememberMe, // Nếu true, cookie sẽ tồn tại kể cả khi đóng trình duyệt
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                };

                // 2. Thực hiện Đăng nhập vào hệ thống Cookie
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                return RedirectToAction("Index", "Home");
            }

            ViewBag.Username = username;
            ViewBag.ErrorMessage = "Sai tài khoản, mật khẩu hoặc tài khoản đã bị khóa!";
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            // Xóa Cookie đăng nhập
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            
            return RedirectToAction("Login", "Account");
        }

        private string HashSha256(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}

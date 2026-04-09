using Microsoft.AspNetCore.Mvc;
using Delivery_System.Models;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

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
        public async Task<IActionResult> Login(string userID, string password)
        {
            if (string.IsNullOrEmpty(userID) || string.IsNullOrEmpty(password))
            {
                ViewBag.ErrorMessage = "Vui lòng nhập đầy đủ thông tin!";
                return View();
            }

            // Chuẩn hóa dữ liệu: Cắt khoảng trắng đầu/cuối
            userID = userID.Trim();
            password = password.Trim();

            string hashedPassword = HashSha256(password);
            var user = await _context.TblUsers
                .FirstOrDefaultAsync(u => u.UserId == userID && u.Password == hashedPassword && u.Status == true);

            if (user != null)
            {
                HttpContext.Session.SetString("UserID", user.UserId);
                HttpContext.Session.SetString("FullName", user.FullName ?? "Người dùng");
                HttpContext.Session.SetString("Role", user.RoleId);
                HttpContext.Session.SetString("Email", user.Email ?? "");
                return RedirectToAction("Index", "Home");
            }

            ViewBag.UserID = userID;
            ViewBag.ErrorMessage = "Sai tài khoản, mật khẩu hoặc tài khoản đã bị khóa!";
            return View();
        }

        [HttpPost]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
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

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using System.Security.Cryptography;
using System.Text;

namespace Delivery_System.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "AD") return RedirectToAction("Index", "Home");

            // Lấy giờ Việt Nam chuẩn (GMT+7)
            var vniTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"));
            var today = vniTime.Date;

            ViewBag.RevenueToday = await _context.TblOrders
                .Where(o => o.CreatedAt != null && o.CreatedAt.Value.Date == today && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null))
                .SumAsync(o => o.Amount ?? 0);

            ViewBag.ActiveStaffCount = await _context.TblWorkShifts.CountAsync(s => s.Status == "ACTIVE");
            var userList = await _context.TblUsers.OrderBy(u => u.RoleId).ToListAsync();
            ViewBag.Announcements = await _context.TblAnnouncements.Include(a => a.CreatedByNavigation).OrderByDescending(a => a.CreatedAt).ToListAsync();

            return View(userList);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleUser(string userID)
        {
            var user = await _context.TblUsers.FindAsync(userID);
            if (user != null)
            {
                user.Status = !(user.Status ?? false);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> SaveUser(string newUserID, string newFullName, string newPassword, string newPhone, string newEmail)
        {
            var user = new TblUser
            {
                UserId = newUserID,
                FullName = newFullName,
                Password = HashSha256(newPassword),
                Phone = newPhone,
                Email = newEmail,
                RoleId = "US",
                Status = true,
                CreatedAt = DateTime.Now
            };
            _context.TblUsers.Add(user);
            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> ChangePassword(string cpUserID, string cpNewPassword)
        {
            var user = await _context.TblUsers.FindAsync(cpUserID);
            if (user != null)
            {
                user.Password = HashSha256(cpNewPassword);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUser(string userID)
        {
            var user = await _context.TblUsers.FindAsync(userID);
            if (user != null && userID != "admin")
            {
                user.Status = false; // Chỉ khóa tài khoản, không xóa khỏi DB
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Đã khóa tài khoản {userID} thành công.";
            }
            return RedirectToAction("Index");
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

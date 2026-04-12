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

        private bool IsAdmin() => HttpContext.Session.GetString("Role") == "AD";

        public async Task<IActionResult> Index()
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            var today = Delivery_System.Helpers.TimeHelper.DateVni();
            var tomorrow = today.AddDays(1);

            // 1. CHẠY SONG SONG: 4 query độc lập không chờ nhau
            var revenueTask = _context.TblOrders.AsNoTracking()
                .Where(o => o.CreatedAt >= today && o.CreatedAt < tomorrow && o.ShipStatus == "Đã chuyển")
                .SumAsync(o => o.Amount ?? 0);

            var staffCountTask = _context.TblWorkShifts.AsNoTracking()
                .CountAsync(s => s.Status == "ACTIVE");

            var userListTask = _context.TblUsers.AsNoTracking()
                .OrderBy(u => u.RoleId)
                .ToListAsync();

            var announcementsTask = _context.TblAnnouncements.AsNoTracking()
                .Include(a => a.CreatedByNavigation)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            await Task.WhenAll(revenueTask, staffCountTask, userListTask, announcementsTask);

            ViewBag.RevenueToday = revenueTask.Result;
            ViewBag.ActiveStaffCount = staffCountTask.Result;
            ViewBag.Announcements = announcementsTask.Result;

            return View(userListTask.Result);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleUser(string userID)
        {
            if (!IsAdmin()) return Forbid();
            if (userID == "admin") 
            {
                TempData["ErrorMessage"] = "Không thể khóa tài khoản quản trị viên tối cao!";
                return RedirectToAction("Index");
            }

            var user = await _context.TblUsers.FirstOrDefaultAsync(u => u.UserId == userID);
            if (user != null)
            {
                user.Status = !(user.Status ?? false);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> SaveUser(string newUserID, string newUsername, string newFullName, string newPassword, string newPhone, string newEmail)
        {
            // Kiểm tra trùng lặp Mã nhân viên hoặc Tên đăng nhập
            var existingUser = await _context.TblUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == newUserID || u.Username == newUsername);

            if (existingUser != null)
            {
                if (existingUser.UserId == newUserID)
                    TempData["ErrorMessage"] = $"Mã nhân viên '{newUserID}' đã tồn tại trên hệ thống!";
                else
                    TempData["ErrorMessage"] = $"Tên đăng nhập '{newUsername}' đã được người khác sử dụng!";
                
                return RedirectToAction("Index");
            }

            var user = new TblUser
            {
                UserId = newUserID,
                Username = newUsername,
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
            TempData["SuccessMessage"] = "Thêm nhân viên mới thành công!";
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
                _context.TblUsers.Remove(user);
                await _context.SaveChangesAsync();
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

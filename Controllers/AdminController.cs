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
            var tomorrow = today.AddDays(1);

            // TỐI ƯU: Sử dụng Range Search (>= và <) thay cho .Date để tận dụng Index idx_orders_createdAt
            // Thêm .AsNoTracking() cho truy vấn chỉ tính toán
            ViewBag.RevenueToday = await _context.TblOrders
                .AsNoTracking()
                .Where(o => o.CreatedAt >= today && o.CreatedAt < tomorrow 
                            && o.ShipStatus == "Đã chuyển" 
                            && (o.IsDeleted == false || o.IsDeleted == null))
                .SumAsync(o => o.Amount ?? 0);

            // TỐI ƯU: Thêm .AsNoTracking() cho đếm số lượng
            ViewBag.ActiveStaffCount = await _context.TblWorkShifts.AsNoTracking().CountAsync(s => s.Status == "ACTIVE");

            // TỐI ƯU: Thêm .AsNoTracking() cho danh sách người dùng
            var userList = await _context.TblUsers.AsNoTracking().OrderBy(u => u.RoleId).ToListAsync();

            // TỐI ƯU: Thêm .AsNoTracking() cho danh sách thông báo
            ViewBag.Announcements = await _context.TblAnnouncements
                .AsNoTracking()
                .Include(a => a.CreatedByNavigation)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return View(userList);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleUser(string userID)
        {
            if (userID == "admin") 
            {
                TempData["ErrorMessage"] = "Không thể khóa tài khoản quản trị viên tối cao!";
                return RedirectToAction("Index");
            }

            var user = await _context.TblUsers.FindAsync(userID);
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

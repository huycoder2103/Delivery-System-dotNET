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

        private decimal ParseSafe(string? val)
        {
            if (string.IsNullOrWhiteSpace(val)) return 0;
            return decimal.TryParse(val, out decimal res) ? res : 0;
        }

        public async Task<IActionResult> Index()
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            var today = Delivery_System.Helpers.TimeHelper.DateVni();
            var tomorrow = today.AddDays(1);

            // Tính doanh thu hôm nay theo logic Report: Tổng (TR + CT) của các đơn ĐÃ GIAO trong ngày
            var dailyOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.CreatedAt >= today && o.CreatedAt < tomorrow && o.ShipStatus == "Đã giao" && (o.IsDeleted == false || o.IsDeleted == null))
                .ToListAsync();
            
            ViewBag.RevenueToday = dailyOrders.Sum(o => ParseSafe(o.Tr) + ParseSafe(o.Ct));

            ViewBag.ActiveStaffCount = await _context.TblWorkShifts.AsNoTracking()
                .CountAsync(s => s.Status == "ACTIVE");

            var userList = await _context.TblUsers.AsNoTracking()
                .Include(u => u.Station)
                .OrderBy(u => u.RoleId)
                .ToListAsync();

            var stationList = await _context.TblStations.AsNoTracking()
                .OrderBy(s => s.StationId)
                .ToListAsync();

            ViewBag.StationList = stationList;

            ViewBag.Announcements = await _context.TblAnnouncements.AsNoTracking()
                .Include(a => a.CreatedByNavigation)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            // Chuẩn bị dữ liệu biểu đồ doanh thu 7 ngày gần nhất (theo logic Report)
            var sevenDaysAgo = today.AddDays(-6);
            var weeklyOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.CreatedAt >= sevenDaysAgo && o.CreatedAt < tomorrow && o.ShipStatus == "Đã giao" && (o.IsDeleted == false || o.IsDeleted == null))
                .ToListAsync();

            var chartLabels = new List<string>();
            var chartData = new List<decimal>();

            for (int i = 6; i >= 0; i--)
            {
                var d = today.AddDays(-i);
                chartLabels.Add(d.ToString("dd/MM"));
                var daySum = weeklyOrders.Where(o => o.CreatedAt!.Value.Date == d)
                    .Sum(o => ParseSafe(o.Tr) + ParseSafe(o.Ct));
                chartData.Add(daySum);
            }

            ViewBag.ChartLabels = chartLabels;
            ViewBag.ChartData = chartData;

            return View(userList);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleUser(string userID)
        {
            if (!IsAdmin()) return Forbid();
            
            var user = await _context.TblUsers.FirstOrDefaultAsync(u => u.UserId == userID);
            if (user != null)
            {
                if (user.RoleId == "AD")
                {
                    TempData["ErrorMessage"] = "Không thể khóa tài khoản quản trị viên!";
                    return RedirectToAction("Index");
                }
                user.Status = !(user.Status ?? false);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> SaveUser(string newUserID, string newUsername, string newFullName, string newPassword, string newPhone, string newEmail, int? newStationID)
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
                StationId = newStationID,
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
        public async Task<IActionResult> UpdateUser(string userId, string fullName, string phone, string email, int? stationId)
        {
            if (!IsAdmin()) return Forbid();
            
            var user = await _context.TblUsers.FindAsync(userId);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy nhân viên!";
                return RedirectToAction("Index");
            }

            user.FullName = fullName;
            user.Phone = phone;
            user.Email = email;
            user.StationId = stationId;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Cập nhật thông tin nhân viên {userId} thành công!";
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUser(string userID)
        {
            var user = await _context.TblUsers.FindAsync(userID);
            if (user != null)
            {
                if (user.RoleId == "AD")
                {
                    TempData["ErrorMessage"] = "Không thể xóa tài khoản quản trị viên!";
                    return RedirectToAction("Index");
                }
                _context.TblUsers.Remove(user);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Xóa nhân viên thành công!";
            }
            return RedirectToAction("Index");
        }

        // --- STATION MANAGEMENT ---

        [HttpPost]
        public async Task<IActionResult> SaveStation(int? stationId, string stationName, string address, string phone)
        {
            if (!IsAdmin()) return Forbid();

            if (stationId.HasValue && stationId.Value > 0)
            {
                // Update
                var existing = await _context.TblStations.FindAsync(stationId.Value);
                if (existing != null)
                {
                    existing.StationName = stationName;
                    existing.Address = address;
                    existing.Phone = phone;
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Cập nhật trạm thành công!";
                }
            }
            else
            {
                // Create
                var station = new TblStation
                {
                    StationName = stationName,
                    Address = address,
                    Phone = phone,
                    IsActive = true
                };
                _context.TblStations.Add(station);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Thêm trạm mới thành công!";
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> ToggleStation(int stationId)
        {
            if (!IsAdmin()) return Forbid();
            var station = await _context.TblStations.FindAsync(stationId);
            if (station != null)
            {
                station.IsActive = !(station.IsActive ?? false);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteStation(int stationId)
        {
            if (!IsAdmin()) return Forbid();
            var station = await _context.TblStations.FindAsync(stationId);
            if (station != null)
            {
                _context.TblStations.Remove(station);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Xóa trạm thành công!";
            }
            return RedirectToAction("Index");
        }

        // --- STAFF STATION MANAGEMENT ---

        [HttpGet]
        public async Task<IActionResult> GetStaffByStation(int stationId)
        {
            if (!IsAdmin()) return Forbid();

            var inStation = await _context.TblUsers.AsNoTracking()
                .Where(u => u.StationId == stationId)
                .Select(u => new { u.UserId, u.FullName, u.Phone })
                .ToListAsync();

            var available = await _context.TblUsers.AsNoTracking()
                .Where(u => u.StationId == null && u.RoleId != "AD")
                .Select(u => new { u.UserId, u.FullName, u.Phone })
                .ToListAsync();

            return Json(new { inStation, available });
        }

        [HttpPost]
        public async Task<IActionResult> AddStaffToStation(string userId, int stationId)
        {
            if (!IsAdmin()) return Forbid();
            var user = await _context.TblUsers.FindAsync(userId);
            if (user != null)
            {
                user.StationId = stationId;
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Không tìm thấy nhân viên" });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveStaffFromStation(string userId)
        {
            if (!IsAdmin()) return Forbid();
            var user = await _context.TblUsers.FindAsync(userId);
            if (user != null)
            {
                user.StationId = null;
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Không tìm thấy nhân viên" });
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

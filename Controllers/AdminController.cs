using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using Delivery_System.Helpers;
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

        private bool IsAdmin() => User.GetRole() == "AD";

        public async Task<IActionResult> Index()
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            // Chỉ lấy số lượng nhân viên đang trong ca làm việc (rất nhẹ)
            ViewBag.ActiveStaffCount = await _context.TblWorkShifts.AsNoTracking()
                .CountAsync(s => s.Status == "ACTIVE");

            // Lấy danh sách nhân viên và trạm
            var userList = await _context.TblUsers.AsNoTracking()
                .Include(u => u.Station)
                .Include(u => u.TblWorkShifts.Where(s => s.Status == "ACTIVE"))
                .OrderBy(u => u.RoleId)
                .ToListAsync();

            ViewBag.StationList = await _context.TblStations.AsNoTracking()
                .OrderBy(s => s.StationId)
                .ToListAsync();

            ViewBag.Announcements = await _context.TblAnnouncements.AsNoTracking()
                .Include(a => a.CreatedByNavigation)
                .OrderByDescending(a => a.CreatedAt)
                .Take(10)
                .ToListAsync();

            return View(userList);
        }

        // TỐI ƯU: Trả về JSON để xử lý AJAX, không load lại cả trang nặng
        [HttpPost]
        public async Task<IActionResult> AdminEndShift(string userId)
        {
            if (!IsAdmin()) return Forbid();

            var activeShift = await _context.TblWorkShifts
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (activeShift != null)
            {
                activeShift.Status = "ENDED";
                activeShift.EndTime = TimeHelper.NowVni();
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Đã kết thúc ca làm việc của nhân viên {userId}!" });
            }
            return Json(new { success = false, message = "Không tìm thấy ca làm việc đang hoạt động." });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleUser(string userID)
        {
            if (!IsAdmin()) return Forbid();
            var user = await _context.TblUsers.FirstOrDefaultAsync(u => u.UserId == userID);
            if (user != null)
            {
                if (user.RoleId == "AD") return Json(new { success = false, message = "Không thể khóa Admin!" });
                user.Status = !(user.Status ?? false);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = user.Status == true ? "Đã mở khóa nhân viên" : "Đã khóa nhân viên" });
            }
            return Json(new { success = false, message = "Không tìm thấy nhân viên" });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUser(string userID)
        {
            if (!IsAdmin()) return Forbid();
            var user = await _context.TblUsers.FirstOrDefaultAsync(u => u.UserId == userID);
            if (user != null)
            {
                if (user.RoleId == "AD") return Json(new { success = false, message = "Không thể xóa Admin!" });
                _context.TblUsers.Remove(user);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Xóa nhân viên thành công!" });
            }
            return Json(new { success = false, message = "Không tìm thấy nhân viên" });
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
                return Json(new { success = true, message = station.IsActive == true ? "Đã kích hoạt trạm" : "Đã tạm dừng trạm" });
            }
            return Json(new { success = false, message = "Không tìm thấy trạm" });
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
                return Json(new { success = true, message = "Xóa trạm thành công!" });
            }
            return Json(new { success = false, message = "Không tìm thấy trạm" });
        }

        [HttpPost]
        public async Task<IActionResult> SaveUser(string newUserID, string newUsername, string newFullName, string newPassword, string newPhone, string newEmail, int? newStationID)
        {
            var existingUser = await _context.TblUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == newUserID || u.Username == newUsername);
            if (existingUser != null)
            {
                return Json(new { success = false, message = (existingUser.UserId == newUserID) ? $"Mã NV '{newUserID}' đã tồn tại!" : $"Tên đăng nhập '{newUsername}' đã tồn tại!" });
            }
            var user = new TblUser { UserId = newUserID, Username = newUsername, FullName = newFullName, Password = HashSha256(newPassword), Phone = newPhone, Email = newEmail, RoleId = "US", StationId = newStationID, Status = true, CreatedAt = TimeHelper.NowVni() };
            _context.TblUsers.Add(user);
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Thêm nhân viên thành công!" });
        }

        [HttpPost]
        public async Task<IActionResult> ChangePassword(string cpUserID, string cpNewPassword)
        {
            var user = await _context.TblUsers.FindAsync(cpUserID);
            if (user != null) 
            { 
                user.Password = HashSha256(cpNewPassword); 
                await _context.SaveChangesAsync(); 
                return Json(new { success = true, message = "Đổi mật khẩu thành công!" });
            }
            return Json(new { success = false, message = "Không tìm thấy người dùng." });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateUser(string userId, string fullName, string phone, string email, int? stationId)
        {
            var user = await _context.TblUsers.FindAsync(userId);
            if (user != null) 
            { 
                user.FullName = fullName; 
                user.Phone = phone; 
                user.Email = email; 
                user.StationId = stationId; 
                await _context.SaveChangesAsync(); 
                return Json(new { success = true, message = "Cập nhật thông tin thành công!" });
            }
            return Json(new { success = false, message = "Không tìm thấy nhân viên." });
        }

        [HttpPost]
        public async Task<IActionResult> SaveStation(int? stationId, string stationName, string address, string phone)
        {
            if (stationId.HasValue && stationId.Value > 0)
            {
                var existing = await _context.TblStations.FindAsync(stationId.Value);
                if (existing != null) 
                { 
                    existing.StationName = stationName; 
                    existing.Address = address; 
                    existing.Phone = phone; 
                    await _context.SaveChangesAsync(); 
                    return Json(new { success = true, message = "Cập nhật trạm thành công!" });
                }
            }
            else
            {
                var station = new TblStation { StationName = stationName, Address = address, Phone = phone, IsActive = true };
                _context.TblStations.Add(station); 
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Thêm trạm mới thành công!" });
            }
            return Json(new { success = false, message = "Có lỗi xảy ra." });
        }

        [HttpGet]
        public async Task<IActionResult> GetStaffByStation(int stationId)
        {
            var inStation = await _context.TblUsers.AsNoTracking().Where(u => u.StationId == stationId).Select(u => new { u.UserId, u.FullName, u.Phone }).ToListAsync();
            var available = await _context.TblUsers.AsNoTracking().Where(u => u.StationId == null && u.RoleId != "AD").Select(u => new { u.UserId, u.FullName, u.Phone }).ToListAsync();
            return Json(new { inStation, available });
        }

        [HttpPost]
        public async Task<IActionResult> AddStaffToStation(string userId, int stationId)
        {
            var user = await _context.TblUsers.FindAsync(userId);
            if (user != null) { user.StationId = stationId; await _context.SaveChangesAsync(); return Json(new { success = true }); }
            return Json(new { success = false });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveStaffFromStation(string userId)
        {
            var user = await _context.TblUsers.FindAsync(userId);
            if (user != null) { user.StationId = null; await _context.SaveChangesAsync(); return Json(new { success = true }); }
            return Json(new { success = false });
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

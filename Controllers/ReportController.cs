using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using Delivery_System.Helpers;

namespace Delivery_System.Controllers
{
    public class ReportController : Controller
    {
        private readonly AppDbContext _context;

        public ReportController(AppDbContext context)
        {
            _context = context;
        }

        // 1. CHỈ TRẢ VỀ KHUNG TRANG (SHELL)
        public IActionResult Index(string? reportDate)
        {
            ViewBag.ReportDate = reportDate ?? TimeHelper.NowVni().ToString("yyyy-MM-dd");
            return View();
        }

        // 2. ACTION: LẤY DỮ LIỆU TAB TỔNG QUAN
        public async Task<IActionResult> GetSummary(string reportDate)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();
            var vniNow = TimeHelper.NowVni();
            DateTime selectedDate = DateTime.Parse(reportDate).Date;
            DateTime tomorrow = selectedDate.AddDays(1);

            if (role == "AD")
            {
                var startOfMonth = new DateTime(vniNow.Year, vniNow.Month, 1);
                var sevenDaysAgo = vniNow.Date.AddDays(-6);

                ViewBag.RevenueDateVal = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow && o.ShipStatus == "Đã giao")
                    .SumAsync(o => (o.Tr ?? 0) + (o.Ct ?? 0));

                ViewBag.OrdersDateVal = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow).CountAsync();

                ViewBag.RevenueWeekly = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= sevenDaysAgo && o.CreatedAt < vniNow.Date.AddDays(1) && o.ShipStatus == "Đã giao")
                    .SumAsync(o => (o.Tr ?? 0) + (o.Ct ?? 0));

                ViewBag.RevenueMonthly = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= startOfMonth && o.CreatedAt < vniNow.Date.AddDays(1) && o.ShipStatus == "Đã giao")
                    .SumAsync(o => (o.Tr ?? 0) + (o.Ct ?? 0));

                ViewBag.RevenueTotalSystem = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.ShipStatus == "Đã giao").SumAsync(o => (o.Tr ?? 0) + (o.Ct ?? 0));

                // Biểu đồ
                var weeklyData = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= sevenDaysAgo && o.CreatedAt < vniNow.Date.AddDays(1) && o.ShipStatus == "Đã giao")
                    .GroupBy(o => o.CreatedAt!.Value.Date)
                    .Select(g => new { Day = g.Key, Total = g.Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0)) }).ToListAsync();

                var labels = new List<string>();
                var data = new List<decimal>();
                for (int i = 6; i >= 0; i--) {
                    var d = vniNow.Date.AddDays(-i);
                    labels.Add(d.ToString("dd/MM"));
                    data.Add(weeklyData.FirstOrDefault(x => x.Day == d)?.Total ?? 0);
                }
                ViewBag.ChartLabels = labels; ViewBag.ChartData = data;
                return PartialView("_TabSummaryAD");
            }
            else
            {
                ViewBag.RevenueDateVal = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffInput == userId && o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow && o.ShipStatus == "Đã giao")
                    .SumAsync(o => (o.Tr ?? 0) + (o.Ct ?? 0));

                ViewBag.OrdersDateVal = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffInput == userId && o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow && o.ShipStatus == "Đã giao")
                    .CountAsync();

                var stats = await _context.TblOrders.AsNoTracking().Where(o => o.StaffInput == userId && o.ShipStatus == "Đã giao")
                    .GroupBy(o => 1).Select(g => new { Count = g.Count(), Revenue = g.Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0)) }).FirstOrDefaultAsync();
                
                ViewBag.DeliveredOrders = stats?.Count ?? 0;
                ViewBag.TotalRevenue = stats?.Revenue ?? 0;
                ViewBag.CurrentShift = await _context.TblWorkShifts.AsNoTracking().FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
                return PartialView("_TabSummaryStaff");
            }
        }

        public async Task<IActionResult> GetHistory()
        {
            var role = User.GetRole();
            var userId = User.GetUserId();
            
            var query = _context.TblWorkShifts.AsNoTracking().AsQueryable();
            if (role != "AD") query = query.Where(s => s.StaffId == userId);

            // TỐI ƯU: Sử dụng Select để MySQL tính toán doanh thu (Revenue) ngay trong câu lệnh SQL
            var shifts = await query.OrderByDescending(s => s.StartTime)
                .Take(20)
                .Select(s => new TblWorkShift {
                    ShiftId = s.ShiftId,
                    StaffId = s.StaffId,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    Status = s.Status,
                    Staff = new TblUser {
                        FullName = s.Staff.FullName
                    },
                    // Tính doanh thu trực tiếp bằng Subquery (MySQL xử lý cực nhanh)
                    Revenue = _context.TblOrders
                        .Where(o => o.ShiftId == s.ShiftId && o.ShipStatus == "Đã giao")
                        .Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0))
                })
                .ToListAsync();

            return PartialView("_TabHistory", shifts);
        }

        // ACTION: ADMIN KẾT THÚC CA HỘ NHÂN VIÊN
        [HttpPost]
        public async Task<IActionResult> AdminEndShift(int id)
        {
            if (User.GetRole() != "AD") return Forbid();

            var shift = await _context.TblWorkShifts.FirstOrDefaultAsync(s => s.ShiftId == id);
            if (shift != null && shift.Status == "ACTIVE")
            {
                shift.Status = "ENDED";
                shift.EndTime = TimeHelper.NowVni();
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Đã kết thúc ca làm việc của nhân viên!" });
            }
            return Json(new { success = false, message = "Không tìm thấy ca làm việc hoặc ca đã kết thúc." });
        }

        // 4. ACTION: LẤY DỮ LIỆU TAB HOẠT ĐỘNG (AD)
        public async Task<IActionResult> GetActivity(string? logDate, int? stationId, string? searchStaff)
        {
            if (User.GetRole() != "AD") return Forbid();
            var vniNow = TimeHelper.NowVni();
            ViewBag.StationList = await _context.TblStations.AsNoTracking().Where(s => s.IsActive == true).ToListAsync();

            DateTime filterDate = string.IsNullOrEmpty(logDate) ? vniNow.Date : DateTime.Parse(logDate).Date;
            DateTime nextDay = filterDate.AddDays(1);

            // Tìm những nhân viên có hoạt động (nhập đơn, giao đơn, hoặc tạo chuyến) trong ngày được chọn
            var staffIdsWithActivity = await _context.TblOrders.AsNoTracking()
                .Where(o => (o.CreatedAt >= filterDate && o.CreatedAt < nextDay))
                .Select(o => o.StaffInput)
                .Union(_context.TblOrders.AsNoTracking()
                    .Where(o => (o.CreatedAt >= filterDate && o.CreatedAt < nextDay))
                    .Select(o => o.StaffReceive))
                .Union(_context.TblTrips.AsNoTracking()
                    .Where(t => t.CreatedAt >= filterDate && t.CreatedAt < nextDay)
                    .Select(t => t.StaffCreated))
                .Where(id => id != null)
                .Distinct()
                .ToListAsync();

            var query = _context.TblUsers.AsNoTracking().Include(u => u.Station).AsQueryable();
            
            // Nếu có filter cụ thể thì ưu tiên filter, nếu không thì hiện những người có hoạt động
            if (stationId.HasValue || !string.IsNullOrEmpty(searchStaff)) {
                if (stationId.HasValue) query = query.Where(u => u.StationId == stationId.Value);
                if (!string.IsNullOrEmpty(searchStaff)) query = query.Where(u => u.UserId.Contains(searchStaff) || u.FullName.Contains(searchStaff));
            } else {
                query = query.Where(u => staffIdsWithActivity.Contains(u.UserId));
            }

            var list = await query.OrderBy(u => u.FullName).ToListAsync();
            ViewBag.LogDate = filterDate.ToString("yyyy-MM-dd");
            ViewBag.SearchStaff = searchStaff;
            ViewBag.SelectedStationId = stationId;

            return PartialView("_TabActivity", list);
        }

        // 5. CÁC ACTION POPUP (DETAILS, DAILYDETAILS) - GIỮ NGUYÊN HOẶC ĐÃ CÓ TRƯỚC ĐÓ
        public async Task<IActionResult> Details(int id) {
            var userId = User.GetUserId(); var role = User.GetRole();
            var shift = await _context.TblWorkShifts.Include(s => s.Staff).FirstOrDefaultAsync(s => s.ShiftId == id);
            if (shift == null) return NotFound();
            if (role != "AD" && shift.StaffId != userId) return Forbid();

            var added = await _context.TblOrders.AsNoTracking().Where(o => o.ShiftId == id && o.StaffInput == shift.StaffId).ToListAsync();
            ViewBag.AddedOrders = added;
            ViewBag.TotalRevenue = added.Where(o => o.ShipStatus == "Đã giao").Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0));
            ViewBag.ReceivedOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.StaffReceive == shift.StaffId && o.ShipStatus == "Đã giao" && o.CreatedAt >= shift.StartTime && (shift.EndTime == null || o.CreatedAt <= shift.EndTime)).ToListAsync();
            ViewBag.CreatedTrips = await _context.TblTrips.AsNoTracking().Include(t => t.Truck).Where(t => t.ShiftId == id).ToListAsync();
            return PartialView("_ShiftDetails", shift);
        }

        public async Task<IActionResult> DailyDetails(string staffId, string date) {
            if (User.GetRole() != "AD") return Forbid();
            DateTime dt = DateTime.Parse(date).Date;
            var staff = await _context.TblUsers.Include(u => u.Station).FirstOrDefaultAsync(u => u.UserId == staffId);
            if (staff == null) return NotFound();

            var added = await _context.TblOrders.AsNoTracking().Where(o => o.StaffInput == staffId && o.CreatedAt >= dt && o.CreatedAt < dt.AddDays(1)).ToListAsync();
            ViewBag.AddedOrders = added;
            ViewBag.ReceivedOrders = await _context.TblOrders.AsNoTracking().Where(o => o.StaffReceive == staffId && o.ShipStatus == "Đã giao" && o.CreatedAt >= dt && o.CreatedAt < dt.AddDays(1)).ToListAsync();
            ViewBag.CreatedTrips = await _context.TblTrips.AsNoTracking().Include(t => t.Truck).Where(t => t.StaffCreated == staffId && t.CreatedAt >= dt && t.CreatedAt < dt.AddDays(1)).ToListAsync();
            ViewBag.TotalRevenue = added.Where(o => o.ShipStatus == "Đã giao").Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0));
            ViewBag.LogDate = date;
            return PartialView("_DailyDetails", staff);
        }
    }
}

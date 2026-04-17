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
        public async Task<IActionResult> GetSummary(string? reportDate)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();
            var vniNow = TimeHelper.NowVni();
            
            DateTime selectedDate;
            if (string.IsNullOrEmpty(reportDate)) {
                selectedDate = vniNow.Date;
            } else {
                try { selectedDate = DateTime.Parse(reportDate).Date; }
                catch { selectedDate = vniNow.Date; }
            }
            
            DateTime tomorrow = selectedDate.AddDays(1);
            ViewBag.SelectedDate = selectedDate.ToString("yyyy-MM-dd");

            if (role == "AD")
            {
                var startOfMonth = new DateTime(vniNow.Year, vniNow.Month, 1);
                var sevenDaysAgo = vniNow.Date.AddDays(-6);

                var stats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.IsDeleted == false || o.IsDeleted == null)
                    .GroupBy(o => 1)
                    .Select(g => new {
                        RevDate = g.Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow).Sum(o => (o.Amount ?? 0)),
                        OrderDate = g.Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow).Count(),
                        RevWeekly = g.Where(o => o.CreatedAt >= sevenDaysAgo).Sum(o => (o.Amount ?? 0)),
                        RevMonthly = g.Where(o => o.CreatedAt >= startOfMonth).Sum(o => (o.Amount ?? 0)),
                        RevTotal = g.Sum(o => (o.Amount ?? 0))
                    })
                    .FirstOrDefaultAsync();

                ViewBag.RevenueDateVal = stats?.RevDate ?? 0m;
                ViewBag.OrdersDateVal = stats?.OrderDate ?? 0;
                ViewBag.RevenueWeekly = stats?.RevWeekly ?? 0m;
                ViewBag.RevenueMonthly = stats?.RevMonthly ?? 0m;
                ViewBag.RevenueTotalSystem = stats?.RevTotal ?? 0m;

                var weeklyData = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= sevenDaysAgo && (o.IsDeleted == false || o.IsDeleted == null))
                    .GroupBy(o => o.CreatedAt!.Value.Date)
                    .Select(g => new { Day = g.Key, Total = g.Sum(o => (o.Amount ?? 0)) })
                    .ToListAsync();

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
                var stats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffInput == userId && (o.IsDeleted == false || o.IsDeleted == null))
                    .GroupBy(o => 1)
                    .Select(g => new {
                        RevDate = g.Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow).Sum(o => (o.Amount ?? 0)),
                        OrderDate = g.Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow).Count(),
                        TotalCount = g.Count(),
                        TotalRev = g.Sum(o => (o.Amount ?? 0))
                    })
                    .FirstOrDefaultAsync();

                ViewBag.RevenueDateVal = stats?.RevDate ?? 0m;
                ViewBag.OrdersDateVal = stats?.OrderDate ?? 0;
                ViewBag.DeliveredOrders = stats?.TotalCount ?? 0;
                ViewBag.TotalRevenue = stats?.TotalRev ?? 0m;
                ViewBag.CurrentShift = await _context.TblWorkShifts.AsNoTracking().FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
                return PartialView("_TabSummaryStaff");
            }
        }

        // 4. ACTION: LẤY DỮ LIỆU TAB HOẠT ĐỘNG (AD)
        public async Task<IActionResult> GetActivity(string? fromDate, string? toDate, int? stationId, string? searchStaff)
        {
            if (User.GetRole() != "AD") return Forbid();
            ViewBag.StationList = await _context.TblStations.AsNoTracking().Where(s => s.IsActive == true).ToListAsync();

            var query = _context.TblWorkShifts.AsNoTracking().Include(s => s.Staff).ThenInclude(u => u.Station).AsQueryable();
            
            bool isSearch = !string.IsNullOrEmpty(fromDate) || !string.IsNullOrEmpty(toDate) || stationId.HasValue || !string.IsNullOrEmpty(searchStaff);
            ViewBag.IsSearchResult = isSearch;

            if (!string.IsNullOrEmpty(fromDate)) {
                DateTime start = DateTime.Parse(fromDate).Date;
                query = query.Where(s => s.StartTime >= start);
            }
            if (!string.IsNullOrEmpty(toDate)) {
                DateTime end = DateTime.Parse(toDate).Date.AddDays(1);
                query = query.Where(s => s.StartTime < end);
            }
            if (stationId.HasValue) query = query.Where(s => s.Staff.StationId == stationId.Value);
            if (!string.IsNullOrEmpty(searchStaff)) query = query.Where(s => s.StaffId.Contains(searchStaff) || s.Staff.FullName.Contains(searchStaff));

            var shifts = await query.OrderByDescending(s => s.StartTime).Take(isSearch ? 100 : 20).ToListAsync();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            var shiftRevenues = await _context.TblOrders.AsNoTracking()
                .Where(o => o.ShiftId.HasValue && shiftIds.Contains(o.ShiftId.Value) && (o.IsDeleted == false || o.IsDeleted == null))
                .GroupBy(o => o.ShiftId)
                .Select(g => new { ShiftId = g.Key!.Value, Total = g.Sum(o => (o.Amount ?? 0)) })
                .ToDictionaryAsync(x => x.ShiftId, x => x.Total);

            foreach (var s in shifts) s.Revenue = shiftRevenues.GetValueOrDefault(s.ShiftId, 0);
            
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.SearchStaff = searchStaff;
            ViewBag.SelectedStationId = stationId;

            return PartialView("_TabActivity", shifts);
        }

        // 5. CÁC ACTION POPUP (DETAILS)
        public async Task<IActionResult> Details(int id) {
            var userId = User.GetUserId(); var role = User.GetRole();
            var shift = await _context.TblWorkShifts.Include(s => s.Staff).FirstOrDefaultAsync(s => s.ShiftId == id);
            if (shift == null) return NotFound();
            if (role != "AD" && shift.StaffId != userId) return Forbid();

            var added = await _context.TblOrders.AsNoTracking().Where(o => o.ShiftId == id && o.StaffInput == shift.StaffId).ToListAsync();
            ViewBag.AddedOrders = added;
            ViewBag.TotalRevenue = added.Sum(o => (o.Amount ?? 0));
            ViewBag.ReceivedOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.StaffReceive == shift.StaffId && o.CreatedAt >= shift.StartTime && (shift.EndTime == null || o.CreatedAt <= shift.EndTime)).ToListAsync();
            ViewBag.CreatedTrips = await _context.TblTrips.AsNoTracking().Include(t => t.Truck).Where(t => t.ShiftId == id).ToListAsync();
            return PartialView("_ShiftDetails", shift);
        }

        [HttpPost]
        public async Task<IActionResult> AdminEndShift(int id)
        {
            if (User.GetRole() != "AD") return Forbid();
            var shift = await _context.TblWorkShifts.FirstOrDefaultAsync(s => s.ShiftId == id);
            if (shift != null && shift.Status == "ACTIVE") {
                shift.Status = "ENDED";
                shift.EndTime = TimeHelper.NowVni();
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Đã kết thúc ca làm việc của nhân viên!" });
            }
            return Json(new { success = false, message = "Không tìm thấy ca làm việc hoặc ca đã kết thúc." });
        }
    }
}

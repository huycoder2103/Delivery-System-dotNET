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
                string dateStr = selectedDate.ToString("dd/MM/yyyy");

                // 1. Tiền cước gửi thu được trong ngày (TR) - Tính theo ngày tạo
                var prepaidDate = await _context.TblOrders.AsNoTracking()
                    .Where(o => (o.IsDeleted == false || o.IsDeleted == null) && o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow)
                    .SumAsync(o => o.Tr ?? 0);

                // 2. Tiền COD/Cước thu khi giao trong ngày (CT) - Tính theo ngày giao (Lọc chuỗi dd/MM/yyyy)
                var codDate = await _context.TblOrders.AsNoTracking()
                    .Where(o => (o.IsDeleted == false || o.IsDeleted == null) && o.ShipStatus == "Đã giao" && o.ReceiveDate != null && o.ReceiveDate.StartsWith(dateStr))
                    .SumAsync(o => o.Ct ?? 0);

                // 3. Số lượng đơn nhập mới
                var ordersCount = await _context.TblOrders.AsNoTracking()
                    .Where(o => (o.IsDeleted == false || o.IsDeleted == null) && o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow)
                    .CountAsync();

                ViewBag.RevenueDateVal = prepaidDate + codDate; // Tổng tiền mặt THỰC THU trong ngày
                ViewBag.TotalPrepaidAD = prepaidDate;
                ViewBag.TotalCODAD = codDate;
                ViewBag.OrdersDateVal = ordersCount;

                // Thống kê Doanh thu (Accrual basis - Ghi nhận ngay khi tạo đơn)
                var stats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.IsDeleted == false || o.IsDeleted == null)
                    .GroupBy(o => 1)
                    .Select(g => new {
                        RevWeekly = g.Where(o => o.CreatedAt >= sevenDaysAgo).Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0)),
                        RevMonthly = g.Where(o => o.CreatedAt >= startOfMonth).Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0)),
                        RevTotal = g.Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0))
                    })
                    .FirstOrDefaultAsync();

                ViewBag.RevenueWeekly = stats?.RevWeekly ?? 0m;
                ViewBag.RevenueMonthly = stats?.RevMonthly ?? 0m;
                ViewBag.RevenueTotalSystem = stats?.RevTotal ?? 0m;

                var weeklyData = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= sevenDaysAgo && (o.IsDeleted == false || o.IsDeleted == null))
                    .GroupBy(o => o.CreatedAt!.Value.Date)
                    .Select(g => new { Day = g.Key, Total = g.Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0)) })
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
                string dateStr = selectedDate.ToString("dd/MM/yyyy");

                // 1. Tiền cước thu từ đơn gửi (TR) - Theo StaffInput & CreatedAt
                var prepaidStats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffInput == userId && (o.IsDeleted == false || o.IsDeleted == null) && o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow)
                    .SumAsync(o => o.Tr ?? 0);

                // 2. Tiền COD/Cước thu khi giao (CT) - Theo StaffReceive & ReceiveDate
                var codStats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffReceive == userId && o.ShipStatus == "Đã giao" && (o.IsDeleted == false || o.IsDeleted == null) && o.ReceiveDate != null && o.ReceiveDate.StartsWith(dateStr))
                    .SumAsync(o => o.Ct ?? 0);

                var orderCount = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffInput == userId && (o.IsDeleted == false || o.IsDeleted == null) && o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow)
                    .CountAsync();

                ViewBag.TotalPrepaid = prepaidStats;
                ViewBag.TotalCOD = codStats;
                ViewBag.CashHolding = prepaidStats + codStats;
                ViewBag.OrdersDateVal = orderCount;
                ViewBag.CurrentShift = await _context.TblWorkShifts.AsNoTracking().FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
                
                return PartialView("_TabSummaryStaff");
            }
        }

        // 4. ACTION: LẤY DỮ LIỆU TAB HOẠT ĐỘNG
        public async Task<IActionResult> GetActivity(string? fromDate, string? toDate, int? stationId, string? searchStaff)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();
            ViewBag.StationList = await _context.TblStations.AsNoTracking().Where(s => s.IsActive == true).ToListAsync();

            var query = _context.TblWorkShifts.AsNoTracking().Include(s => s.Staff).ThenInclude(u => u.Station).AsQueryable();
            if (role != "AD") query = query.Where(s => s.StaffId == userId);

            bool isSearch = !string.IsNullOrEmpty(fromDate) || !string.IsNullOrEmpty(toDate) || stationId.HasValue || !string.IsNullOrEmpty(searchStaff);
            ViewBag.IsSearchResult = isSearch;

            DateTime? start = null; DateTime? end = null;
            if (!string.IsNullOrEmpty(fromDate)) { start = DateTime.Parse(fromDate).Date; query = query.Where(s => s.StartTime >= start); }
            if (!string.IsNullOrEmpty(toDate)) { end = DateTime.Parse(toDate).Date.AddDays(1); query = query.Where(s => s.StartTime < end); }

            if (role == "AD") {
                if (stationId.HasValue) query = query.Where(s => s.Staff.StationId == stationId.Value);
                if (!string.IsNullOrEmpty(searchStaff)) query = query.Where(s => s.StaffId.Contains(searchStaff) || s.Staff.FullName.Contains(searchStaff));
            }

            var shifts = await query.OrderByDescending(s => s.StartTime).Take(isSearch ? 100 : 20).ToListAsync();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();
            var staffIds = shifts.Select(s => s.StaffId).Distinct().ToList();

            // 1. LẤY BATCH TẤT CẢ ĐƠN HÀNG CÓ LIÊN QUAN ĐẾN NHÂN VIÊN TRONG DANH SÁCH
            var allOrdersInPeriod = await _context.TblOrders.AsNoTracking()
                .Where(o => (staffIds.Contains(o.StaffInput!) || staffIds.Contains(o.StaffReceive!)) && (o.IsDeleted == false || o.IsDeleted == null))
                .Select(o => new { o.StaffInput, o.StaffReceive, o.CreatedAt, o.ReceiveDate, o.Tr, o.Ct, o.ShipStatus })
                .ToListAsync();

            // 2. PHÂN BỔ DỮ LIỆU TRONG BỘ NHỚ
            foreach (var s in shifts) {
                var sEndTime = s.EndTime ?? TimeHelper.NowVni();
                
                // Tiền cước gửi (TR) - Thu khi nhập đơn
                decimal prepaid = allOrdersInPeriod
                    .Where(o => o.StaffInput == s.StaffId && o.CreatedAt >= s.StartTime.AddMinutes(-1) && o.CreatedAt <= sEndTime.AddMinutes(1))
                    .Sum(o => o.Tr ?? 0);

                // Tiền COD (CT) - Thu khi giao đơn
                decimal cod = allOrdersInPeriod
                    .Where(o => o.StaffReceive == s.StaffId && o.ShipStatus == "Đã giao")
                    .Where(o => {
                        if (DateTime.TryParseExact(o.ReceiveDate, "dd/MM/yyyy HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime dDate))
                            return dDate >= s.StartTime.AddMinutes(-1) && dDate <= sEndTime.AddMinutes(1);
                        return false;
                    })
                    .Sum(o => o.Ct ?? 0);

                s.Revenue = prepaid + cod;
            }
            
            ViewBag.FromDate = fromDate; ViewBag.ToDate = toDate;
            ViewBag.SearchStaff = searchStaff; ViewBag.SelectedStationId = stationId;

            return PartialView("_TabActivity", shifts);
        }

        // 5. CÁC ACTION POPUP (DETAILS)
        public async Task<IActionResult> Details(int id) {
            var userId = User.GetUserId(); var role = User.GetRole();
            var shift = await _context.TblWorkShifts.Include(s => s.Staff).FirstOrDefaultAsync(s => s.ShiftId == id);
            if (shift == null) return NotFound();
            if (role != "AD" && shift.StaffId != userId) return Forbid();

            // 1. Đơn hàng đã NHẬP MỚI trong ca (Cước gửi)
            var addedOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.ShiftId == id && (o.IsDeleted == false || o.IsDeleted == null))
                .ToListAsync();
            ViewBag.AddedOrders = addedOrders;
            ViewBag.TotalPrepaid = addedOrders.Sum(o => o.Tr ?? 0);
            
            // 2. Chuyến xe đã tạo (xuất trạm) trong ca
            ViewBag.CreatedTrips = await _context.TblTrips.AsNoTracking().Include(t => t.Truck).Where(t => t.ShiftId == id).ToListAsync();

            // 3. Chuyến xe đã tiếp nhận (bấm Xe Đến) trong ca
            var rawArrivedTrips = await _context.TblTrips.AsNoTracking().Include(t => t.Truck)
                .Where(t => t.Status == "Đã đến" && t.Notes != null && t.Notes.Contains("[ARRIVED] " + shift.StaffId))
                .ToListAsync();

            var shiftEnd = shift.EndTime ?? TimeHelper.NowVni();
            var arrivedInShift = new List<TblTrip>();

            foreach (var t in rawArrivedTrips) {
                try {
                    // Cấu trúc: [ARRIVED] NV001 | 20/04 15:30
                    var parts = t.Notes!.Split('|');
                    if (parts.Length > 1) {
                        var datePart = parts[1].Trim(); // "20/04 15:30"
                        
                        // Thử nghiệm Parse với nhiều định dạng khả thi
                        string[] formats = { "dd/MM HH:mm", "d/M HH:mm", "dd/MM H:m" };
                        if (DateTime.TryParseExact(datePart, formats, null, System.Globalization.DateTimeStyles.None, out DateTime parsedDate)) {
                            // Gán năm của ca vào ngày đã parse
                            var arrivalDate = new DateTime(shift.StartTime.Year, parsedDate.Month, parsedDate.Day, parsedDate.Hour, parsedDate.Minute, 0);
                            
                            // So sánh với biên thời gian rộng hơn (sai số 2 phút)
                            if (arrivalDate >= shift.StartTime.AddMinutes(-2) && arrivalDate <= shiftEnd.AddMinutes(2)) {
                                arrivedInShift.Add(t);
                            }
                        }
                    }
                } catch { /* Bỏ qua nếu dòng đó bị lỗi format */ }
            }
            ViewBag.ArrivedTrips = arrivedInShift;
// 4. Đơn hàng thực tế GIAO KHÁCH trong ca (Tiền COD)
var delivered = await _context.TblOrders.AsNoTracking()
    .Where(o => o.StaffReceive == shift.StaffId && o.ShipStatus == "Đã giao" && (o.IsDeleted == false || o.IsDeleted == null))
    .ToListAsync();

var deliveredInShift = new List<TblOrder>();
foreach (var o in delivered) {
    if (DateTime.TryParseExact(o.ReceiveDate, "dd/MM/yyyy HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime dDate)) {
        // Mở rộng biên thời gian 1 phút để tránh lỗi làm tròn
        if (dDate >= shift.StartTime.AddMinutes(-1) && dDate <= shiftEnd.AddMinutes(1)) {
            deliveredInShift.Add(o);
        }
    }
}
ViewBag.DeliveredOrders = deliveredInShift;
ViewBag.TotalCOD = deliveredInShift.Sum(o => o.Ct ?? 0);
            
            // TỔNG THU CA = Cước gửi + Tiền COD
            ViewBag.TotalRevenue = (decimal)ViewBag.TotalPrepaid + (decimal)ViewBag.TotalCOD;
            
            return PartialView("_ShiftDetails", shift);
        }

        // 6. ACTION: LẤY DỮ LIỆU DOANH THU THEO TRẠM
        public async Task<IActionResult> GetStationSummary(string? fromDate, string? toDate)
        {
            if (User.GetRole() != "AD") return Forbid();

            DateTime start = string.IsNullOrEmpty(fromDate) ? TimeHelper.NowVni().Date : DateTime.Parse(fromDate).Date;
            DateTime end = string.IsNullOrEmpty(toDate) ? TimeHelper.NowVni().Date : DateTime.Parse(toDate).Date;
            DateTime tomorrow = end.AddDays(1);

            var stations = await _context.TblStations.AsNoTracking().Where(s => s.IsActive == true).ToListAsync();
            
            // 1. Lấy tất cả đơn hàng có khả năng liên quan trong khoảng thời gian
            var orders = await _context.TblOrders.AsNoTracking()
                .Where(o => (o.IsDeleted == false || o.IsDeleted == null))
                .Where(o => (o.CreatedAt >= start && o.CreatedAt < tomorrow) || 
                            (o.ShipStatus == "Đã giao" && o.ReceiveDate != null))
                .Select(o => new { o.SendStation, o.ReceiveStation, o.CreatedAt, o.ReceiveDate, o.Tr, o.Ct, o.ShipStatus })
                .ToListAsync();

            var result = new List<StationRevenueViewModel>();

            foreach (var s in stations)
            {
                // Tiền cước thu tại trạm (Prepaid TR)
                var prepaid = orders.Where(o => o.SendStation == s.StationName && o.CreatedAt >= start && o.CreatedAt < tomorrow)
                                    .Sum(o => o.Tr ?? 0);

                // Tiền COD thu tại trạm khi khách nhận (CT)
                var cod = orders.Where(o => o.ReceiveStation == s.StationName && o.ShipStatus == "Đã giao" &&
                    DateTime.TryParseExact(o.ReceiveDate, "dd/MM/yyyy HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime dDate) &&
                    dDate >= start && dDate < tomorrow).Sum(o => o.Ct ?? 0);

                result.Add(new StationRevenueViewModel {
                    StationName = s.StationName,
                    TotalPrepaid = prepaid,
                    TotalCOD = cod,
                    TotalRevenue = prepaid + cod
                });
            }

            ViewBag.FromDate = start.ToString("yyyy-MM-dd");
            ViewBag.ToDate = end.ToString("yyyy-MM-dd");

            return PartialView("_TabStationSummary", result.OrderByDescending(x => x.TotalRevenue).ToList());
        }

        public class StationRevenueViewModel {
            public string StationName { get; set; } = "";
            public decimal TotalPrepaid { get; set; }
            public decimal TotalCOD { get; set; }
            public decimal TotalRevenue { get; set; }
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

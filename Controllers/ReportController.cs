using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using Delivery_System.Helpers;
using ClosedXML.Excel;
using System.IO;

namespace Delivery_System.Controllers
{
    public class ReportController : Controller
    {
        private readonly AppDbContext _context;

        public ReportController(AppDbContext context)
        {
            _context = context;
        }

        // --- ACTION XUẤT EXCEL CHI TIẾT ---
        [HttpGet]
        public async Task<IActionResult> ExportExcel(string? reportDate, int? stationId)
        {
            var vniNow = TimeHelper.NowVni();
            DateTime selectedDate;
            if (string.IsNullOrEmpty(reportDate)) selectedDate = vniNow.Date;
            else if (!DateTime.TryParse(reportDate, out selectedDate)) selectedDate = vniNow.Date;
            
            DateTime tomorrow = selectedDate.AddDays(1);
            string dateStr = selectedDate.ToString("dd/MM/yyyy");

            // 1. Lấy danh sách trạm cần xuất
            var stationsQuery = _context.TblStations.AsNoTracking().Where(s => s.IsActive == true);
            if (stationId.HasValue) stationsQuery = stationsQuery.Where(s => s.StationId == stationId.Value);
            var stations = await stationsQuery.ToListAsync();

            // 2. Lấy tất cả đơn hàng có liên quan đến ngày này (Nhập hoặc Giao)
            var orders = await _context.TblOrders.AsNoTracking()
                .Where(o => (o.IsDeleted == false || o.IsDeleted == null))
                .Where(o => (o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow) || 
                            (o.ShipStatus == "Đã giao" && o.ReceiveDate != null && o.ReceiveDate.StartsWith(dateStr)))
                .ToListAsync();

            // 3. Lấy tất cả nhân viên có hoạt động (Shift) trong ngày này
            var shiftsQuery = _context.TblWorkShifts.AsNoTracking()
                .Include(s => s.Staff)
                .Where(s => s.StartTime >= selectedDate && s.StartTime < tomorrow);
            
            if (stationId.HasValue) shiftsQuery = shiftsQuery.Where(s => s.Staff.StationId == stationId.Value);
            var shifts = await shiftsQuery.ToListAsync();

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("BaoCaoChiTiet");
                int currentRow = 1;

                // STYLE CHUNG
                worksheet.Column(1).Width = 15; // Mã đơn
                worksheet.Column(2).Width = 25; // Tên hàng
                worksheet.Column(3).Width = 15; // Loại thao tác
                worksheet.Column(4).Width = 15; // Trạng thái
                worksheet.Column(5).Width = 12; // Cước TR
                worksheet.Column(6).Width = 12; // Thu hộ CT
                worksheet.Column(7).Width = 15; // Mã Chuyến
                worksheet.Column(8).Width = 20; // Thời gian

                // TIÊU ĐỀ CHÍNH
                var titleCell = worksheet.Cell(currentRow, 1);
                titleCell.Value = "BÁO CÁO CHI TIẾT HOẠT ĐỘNG CA LÀM VIỆC - NGÀY " + selectedDate.ToString("dd/MM/yyyy");
                titleCell.Style.Font.Bold = true;
                titleCell.Style.Font.FontSize = 16;
                worksheet.Range(currentRow, 1, currentRow, 8).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                currentRow += 2;

                foreach (var st in stations)
                {
                    // Lấy tất cả các ca làm việc thuộc trạm này trong ngày
                    var shiftsInStation = shifts.Where(s => s.Staff.StationId == st.StationId)
                                                .OrderBy(s => s.StartTime)
                                                .ToList();
                    
                    if (!shiftsInStation.Any()) continue;

                    // HEADER TRẠM
                    var stationRange = worksheet.Range(currentRow, 1, currentRow, 8);
                    stationRange.Merge().Value = "🏪 TRẠM: " + st.StationName.ToUpper() + " (ID: " + st.StationId + ")";
                    stationRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
                    stationRange.Style.Font.FontColor = XLColor.White;
                    stationRange.Style.Font.Bold = true;
                    currentRow++;

                    foreach (var shift in shiftsInStation)
                    {
                        var staff = shift.Staff;
                        var sStart = shift.StartTime;
                        var sEnd = shift.EndTime ?? vniNow;

                        // HEADER CA LÀM VIỆC (Có Mã ca)
                        var shiftRange = worksheet.Range(currentRow, 1, currentRow, 8);
                        string shiftTimeStr = $"{sStart:HH:mm} - {(shift.EndTime.HasValue ? shift.EndTime.Value.ToString("HH:mm") : "Đang trực")}";
                        shiftRange.Merge().Value = $"👤 NV: {staff.FullName} ({staff.UserId}) | 🆔 MÃ CA: #{shift.ShiftId} | ⏱️ {shiftTimeStr}";
                        shiftRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");
                        shiftRange.Style.Font.Bold = true;
                        shiftRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        currentRow++;

                        // HEADER BẢNG DỮ LIỆU
                        string[] headers = { "Mã Đơn", "Tên Hàng", "Thao Tác", "Trạng Thái", "Cước TR", "Thu Hộ CT", "Mã Chuyến", "Thời Gian" };
                        for (int i = 0; i < headers.Length; i++)
                        {
                            var cell = worksheet.Cell(currentRow, i + 1);
                            cell.Value = headers[i];
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        }
                        currentRow++;

                        // LẤY DỮ LIỆU ĐƠN HÀNG THUỘC CA NÀY
                        // 1. Đơn nhập mới gắn với ShiftId này
                        var staffOrdersInput = orders.Where(o => o.ShiftId == shift.ShiftId).ToList();
                        
                        // 2. Đơn giao khách bởi nhân viên này trong khung giờ của ca
                        var staffOrdersDeliver = orders.Where(o => o.StaffReceive == staff.UserId && o.ShipStatus == "Đã giao" && o.ReceiveDate != null)
                            .Where(o => {
                                if (DateTime.TryParseExact(o.ReceiveDate, "dd/MM/yyyy HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime dDate))
                                    return dDate >= sStart.AddMinutes(-1) && dDate <= sEnd.AddMinutes(1);
                                return false;
                            }).ToList();

                        var allStaffActions = new List<dynamic>();
                        foreach(var o in staffOrdersInput) allStaffActions.Add(new { OrderId = o.OrderId, Item = o.ItemName, Type = "NHẬP ĐƠN", Status = o.ShipStatus, Tr = o.Tr ?? 0, Ct = 0m, Trip = o.TripId ?? "-", Time = o.CreatedAt?.ToString("HH:mm") });
                        foreach(var o in staffOrdersDeliver) allStaffActions.Add(new { OrderId = o.OrderId, Item = o.ItemName, Type = "GIAO HÀNG", Status = "Đã giao", Tr = 0m, Ct = o.Ct ?? 0, Trip = o.TripId ?? "-", Time = o.ReceiveDate?.Split(' ')[1] ?? "-" });

                        decimal sumTr = 0; decimal sumCt = 0;
                        foreach (var action in allStaffActions.OrderBy(a => a.Time))
                        {
                            worksheet.Cell(currentRow, 1).Value = action.OrderId;
                            worksheet.Cell(currentRow, 2).Value = action.Item;
                            worksheet.Cell(currentRow, 3).Value = action.Type;
                            worksheet.Cell(currentRow, 4).Value = action.Status;
                            worksheet.Cell(currentRow, 5).Value = action.Tr;
                            worksheet.Cell(currentRow, 6).Value = action.Ct;
                            worksheet.Cell(currentRow, 7).Value = action.Trip;
                            worksheet.Cell(currentRow, 8).Value = action.Time;
                            
                            worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0";
                            worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0";
                            
                            if (action.Type == "GIAO HÀNG") worksheet.Cell(currentRow, 3).Style.Font.FontColor = XLColor.Green;
                            else worksheet.Cell(currentRow, 3).Style.Font.FontColor = XLColor.Blue;

                            sumTr += action.Tr; sumCt += action.Ct;
                            currentRow++;
                        }

                        // TỔNG CỘNG CA LÀM VIỆC
                        var footerRange = worksheet.Range(currentRow, 1, currentRow, 4);
                        footerRange.Merge().Value = "TỔNG CỘNG CA #" + shift.ShiftId + ":";
                        footerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        footerRange.Style.Font.Bold = true;
                        
                        worksheet.Cell(currentRow, 5).Value = sumTr;
                        worksheet.Cell(currentRow, 5).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0";
                        
                        worksheet.Cell(currentRow, 6).Value = sumCt;
                        worksheet.Cell(currentRow, 6).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0";

                        // DÒNG TỔNG CỘNG CUỐI CÙNG (TR + CT)
                        currentRow++;
                        var remitRange = worksheet.Range(currentRow, 1, currentRow, 4);
                        remitRange.Merge().Value = "💰 SỐ TIỀN NHÂN VIÊN PHẢI NỘP CA #" + shift.ShiftId + ":";
                        remitRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        remitRange.Style.Font.Bold = true;
                        remitRange.Style.Font.FontColor = XLColor.Red;

                        var totalCell = worksheet.Cell(currentRow, 5);
                        totalCell.Value = sumTr + sumCt;
                        totalCell.Style.Font.Bold = true;
                        totalCell.Style.Font.FontSize = 12;
                        totalCell.Style.Font.FontColor = XLColor.Red;
                        totalCell.Style.NumberFormat.Format = "#,##0";
                        worksheet.Range(currentRow, 5, currentRow, 6).Merge(); // Gộp 2 cột TR và CT cho con số tổng cuối

                        currentRow += 2;
                    }
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCao_ChiTiet_{selectedDate:ddMMyyyy}.xlsx");
                }
            }
        }

        // 1. CHỈ TRẢ VỀ KHUNG TRANG (SHELL)
        public async Task<IActionResult> Index(string? reportDate)
        {
            ViewBag.ReportDate = reportDate ?? TimeHelper.NowVni().ToString("yyyy-MM-dd");
            ViewBag.StationList = await _context.TblStations.AsNoTracking().Where(s => s.IsActive == true).ToListAsync();
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

        // 3. ACTION: LẤY DANH SÁCH ĐƠN HÀNG ĐÃ GIAO TRONG NGÀY (CỦA NHÂN VIÊN)
        public async Task<IActionResult> GetDeliveredOrders(string? reportDate)
        {
            var userId = User.GetUserId();
            var vniNow = TimeHelper.NowVni();
            
            DateTime selectedDate;
            if (string.IsNullOrEmpty(reportDate)) {
                selectedDate = vniNow.Date;
            } else {
                try { selectedDate = DateTime.Parse(reportDate).Date; }
                catch { selectedDate = vniNow.Date; }
            }
            
            string dateStr = selectedDate.ToString("dd/MM/yyyy");

            var deliveredOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.StaffReceive == userId && o.ShipStatus == "Đã giao" && (o.IsDeleted == false || o.IsDeleted == null) && o.ReceiveDate != null && o.ReceiveDate.StartsWith(dateStr))
                .OrderByDescending(o => o.ReceiveDate)
                .ToListAsync();

            ViewBag.SelectedDate = dateStr;
            return PartialView("_DeliveredOrdersModal", deliveredOrders);
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

            // 1. TÍNH TOÁN DOANH THU CHO CÁC CA ĐANG HOẠT ĐỘNG (Dùng SQL GroupBy để tối ưu)
            var activeShiftIds = shifts.Where(s => s.Status != "ENDED").Select(s => s.ShiftId).ToList();
            var activeShiftStats = new Dictionary<int, decimal>();

            if (activeShiftIds.Any())
            {
                // Tiền cước gửi (TR) của các ca đang ACTIVE
                var prepaidStats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.ShiftId.HasValue && activeShiftIds.Contains(o.ShiftId.Value) && (o.IsDeleted == false || o.IsDeleted == null))
                    .GroupBy(o => o.ShiftId!.Value)
                    .Select(g => new { ShiftId = g.Key, Total = g.Sum(o => o.Tr ?? 0) })
                    .ToListAsync();

                // Tiền COD (CT) của các ca đang ACTIVE (Dựa trên StaffReceive và thời gian giao)
                // Lưu ý: Phần này vẫn phải xử lý cẩn thận vì ReceiveDate là string
                // Chúng ta sẽ lấy dữ liệu gộp theo Staff để giảm tải
                foreach (var s in shifts.Where(s => s.Status != "ENDED"))
                {
                    var sEndTime = s.EndTime ?? TimeHelper.NowVni();
                    var prepaid = prepaidStats.FirstOrDefault(x => x.ShiftId == s.ShiftId)?.Total ?? 0;
                    
                    // Chỉ lấy COD của nhân viên này trong khoảng thời gian ca
                    var cod = await _context.TblOrders.AsNoTracking()
                        .Where(o => o.StaffReceive == s.StaffId && o.ShipStatus == "Đã giao" && (o.IsDeleted == false || o.IsDeleted == null))
                        .Where(o => o.ReceiveDate != null)
                        .Select(o => new { o.Ct, o.ReceiveDate })
                        .ToListAsync();

                    decimal totalCod = 0;
                    foreach (var o in cod)
                    {
                        if (DateTime.TryParseExact(o.ReceiveDate, "dd/MM/yyyy HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime dDate))
                        {
                            if (dDate >= s.StartTime.AddMinutes(-1) && dDate <= sEndTime.AddMinutes(1))
                                totalCod += o.Ct ?? 0;
                        }
                    }
                    s.Revenue = prepaid + totalCod;
                }
            }

            // 2. GÁN DOANH THU CHO CÁC CA ĐÃ KẾT THÚC (Dùng dữ liệu đã chốt)
            foreach (var s in shifts.Where(s => s.Status == "ENDED"))
            {
                s.Revenue = s.TotalPrepaid + s.TotalCod;
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

            var shiftStart = shift.StartTime;
            var shiftEnd = shift.EndTime ?? TimeHelper.NowVni();

            // 1. Đơn hàng đã NHẬP MỚI trong ca (Lọc theo Staff + Thời gian để bao quát cả Admin)
            var addedOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.StaffInput == shift.StaffId && (o.IsDeleted == false || o.IsDeleted == null))
                .Where(o => o.CreatedAt >= shiftStart.AddMinutes(-1) && o.CreatedAt <= shiftEnd.AddMinutes(1))
                .ToListAsync();
            ViewBag.AddedOrders = addedOrders;
            ViewBag.TotalPrepaid = addedOrders.Sum(o => o.Tr ?? 0);
            
            // 2. Chuyến xe đã tạo (xuất trạm) trong ca
            ViewBag.CreatedTrips = await _context.TblTrips.AsNoTracking().Include(t => t.Truck)
                .Where(t => t.StaffCreated == shift.StaffId)
                .Where(t => t.CreatedAt >= shiftStart.AddMinutes(-1) && t.CreatedAt <= shiftEnd.AddMinutes(1))
                .ToListAsync();

            // 3. Chuyến xe đã tiếp nhận (bấm Xe Đến) trong ca
            var rawArrivedTrips = await _context.TblTrips.AsNoTracking().Include(t => t.Truck)
                .Where(t => t.Status == "Đã đến" && t.Notes != null && t.Notes.Contains("[ARRIVED] " + shift.StaffId))
                .ToListAsync();

            var arrivedInShift = new List<TblTrip>();
            string[] formats = { "dd/MM HH:mm", "d/M HH:mm", "dd/MM H:m" };

            foreach (var t in rawArrivedTrips) {
                var parts = t.Notes!.Split('|');
                if (parts.Length > 1) {
                    var datePart = parts[1].Trim();
                    if (DateTime.TryParseExact(datePart, formats, null, System.Globalization.DateTimeStyles.None, out DateTime parsedDate)) {
                        // LOGIC ĐOÁN NĂM THÔNG MINH:
                        // Thử năm của StartTime
                        var d1 = new DateTime(shiftStart.Year, parsedDate.Month, parsedDate.Day, parsedDate.Hour, parsedDate.Minute, 0);
                        // Thử năm của EndTime (phòng trường hợp giao thừa)
                        var d2 = new DateTime(shiftEnd.Year, parsedDate.Month, parsedDate.Day, parsedDate.Hour, parsedDate.Minute, 0);
                        
                        if ((d1 >= shiftStart.AddMinutes(-2) && d1 <= shiftEnd.AddMinutes(2))) arrivedInShift.Add(t);
                        else if (d1 != d2 && (d2 >= shiftStart.AddMinutes(-2) && d2 <= shiftEnd.AddMinutes(2))) arrivedInShift.Add(t);
                    }
                }
            }
            ViewBag.ArrivedTrips = arrivedInShift;

            // 4. Đơn hàng thực tế GIAO KHÁCH trong ca (Tiền COD)
            var delivered = await _context.TblOrders.AsNoTracking()
                .Where(o => o.StaffReceive == shift.StaffId && o.ShipStatus == "Đã giao" && (o.IsDeleted == false || o.IsDeleted == null))
                .ToListAsync();

            var deliveredInShift = new List<TblOrder>();
            foreach (var o in delivered) {
                if (DateTime.TryParseExact(o.ReceiveDate, "dd/MM/yyyy HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime dDate)) {
                    if (dDate >= shiftStart.AddMinutes(-2) && dDate <= shiftEnd.AddMinutes(2)) {
                        deliveredInShift.Add(o);
                    }
                }
            }
            ViewBag.DeliveredOrders = deliveredInShift;
            ViewBag.TotalCOD = deliveredInShift.Sum(o => o.Ct ?? 0);
            
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
            
            // 1. Lọc đơn hàng có khả năng liên quan (Dùng SQL lọc sơ bộ để giảm tải RAM)
            // Tạo danh sách các chuỗi ngày (dd/MM/yyyy) trong khoảng từ start đến end
            var datePatterns = new List<string>();
            for (var dt = start; dt <= end; dt = dt.AddDays(1)) {
                datePatterns.Add(dt.ToString("dd/MM/yyyy"));
            }

            var orders = await _context.TblOrders.AsNoTracking()
                .Where(o => (o.IsDeleted == false || o.IsDeleted == null))
                .Where(o => (o.CreatedAt >= start && o.CreatedAt < tomorrow) || 
                            (o.ShipStatus == "Đã giao" && o.ReceiveDate != null && datePatterns.Any(p => o.ReceiveDate.StartsWith(p))))
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
                var endTime = TimeHelper.NowVni();
                
                // TÍNH TOÁN CÁC CHỈ SỐ TRƯỚC KHI ĐÓNG CA (Tương tự HomeController)
                // 1. Tổng TR (Cước gửi) từ các đơn đã nhập trong ca
                var totalPrepaid = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.ShiftId == shift.ShiftId && (o.IsDeleted == false || o.IsDeleted == null))
                    .SumAsync(o => o.Tr ?? 0);

                // 2. Tổng CT (COD/Cước thu khi giao) từ các đơn đã giao trong ca
                var deliveredOrders = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffReceive == shift.StaffId && o.ShipStatus == "Đã giao" && (o.IsDeleted == false || o.IsDeleted == null))
                    .Select(o => new { o.Ct, o.ReceiveDate })
                    .ToListAsync();

                decimal totalCod = 0;
                foreach (var o in deliveredOrders)
                {
                    if (DateTime.TryParseExact(o.ReceiveDate, "dd/MM/yyyy HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime dDate))
                    {
                        if (dDate >= shift.StartTime.AddMinutes(-1) && dDate <= endTime.AddMinutes(1))
                        {
                            totalCod += o.Ct ?? 0;
                        }
                    }
                }

                // 3. Tổng số đơn hàng đã nhập trong ca
                var orderCount = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.ShiftId == shift.ShiftId && (o.IsDeleted == false || o.IsDeleted == null))
                    .CountAsync();

                shift.Status = "ENDED";
                shift.EndTime = endTime;
                shift.TotalPrepaid = totalPrepaid;
                shift.TotalCod = totalCod;
                shift.OrderCount = orderCount;

                // TỰ ĐỘNG ĐỔ DATA VÀO BẢNG KẾ TOÁN (tblShiftAccounting)
                var accounting = new TblShiftAccounting
                {
                    ShiftId = shift.ShiftId,
                    SystemPrepaid = totalPrepaid,
                    SystemCod = totalCod,
                    TotalSystem = totalPrepaid + totalCod,
                    Status = 0 // Pending: Chờ kế toán xác nhận
                };
                _context.TblShiftAccountings.Add(accounting);

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Đã kết thúc ca làm việc và chuyển dữ liệu kế toán!" });
            }
            return Json(new { success = false, message = "Không tìm thấy ca làm việc hoặc ca đã kết thúc." });
        }
    }
}

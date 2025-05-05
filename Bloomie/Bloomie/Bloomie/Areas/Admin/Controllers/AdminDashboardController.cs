using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;
using Bloomie.Models.ViewModels;
using OfficeOpenXml;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AdminDashboardController> _logger;

        public AdminDashboardController(ApplicationDbContext context, ILogger<AdminDashboardController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string searchQuery = null, int? categoryId = null)
        {
            const int lowStockThreshold = 10;

            var today = DateTime.Today;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var endOfWeek = startOfWeek.AddDays(7);

            var dashboardViewModel = new AdminDashboardViewModel
            {
                TotalProducts = await _context.Products.CountAsync(p => p.IsActive),
                TotalOrders = await _context.Orders.CountAsync(),
                TotalSuppliers = await _context.Suppliers.CountAsync(),
                TotalUsers = await _context.Users.CountAsync(),
                TotalCategories = await _context.Categories.CountAsync(),
                TotalPromotions = await _context.Promotions.CountAsync(),
                TotalAccessCount = await _context.UserAccessLogs
                    .CountAsync(log => log.UserId != null && (log.Url == "/" || log.Url == "/Home/Index")),
                LowStockProductCount = await _context.Products
                    .CountAsync(p => p.IsActive && p.Quantity > 0 && p.Quantity <= lowStockThreshold),
                OutOfStockProductCount = await _context.Products
                    .CountAsync(p => p.IsActive && p.Quantity == 0),
                DailyRevenue = await _context.Orders
                    .Where(o => o.OrderDate.Date == today)
                    .SumAsync(o => o.TotalPrice),
                WeeklyRevenue = await _context.Orders
                    .Where(o => o.OrderDate >= startOfWeek && o.OrderDate < endOfWeek)
                    .SumAsync(o => o.TotalPrice)
            };

            var lowStockQuery = _context.Products
                .Include(p => p.Category)
                .Where(p => p.IsActive && p.Quantity > 0 && p.Quantity <= lowStockThreshold);

            if (!string.IsNullOrEmpty(searchQuery))
            {
                lowStockQuery = lowStockQuery.Where(p => p.Name.Contains(searchQuery));
            }

            if (categoryId.HasValue)
            {
                lowStockQuery = lowStockQuery.Where(p => p.CategoryId == categoryId);
            }

            dashboardViewModel.LowStockProducts = await lowStockQuery
                .OrderBy(p => p.Quantity)
                .Take(5)
                .ToListAsync();

            dashboardViewModel.Categories = await _context.Categories.ToListAsync();

            dashboardViewModel.Notifications = new List<string>();
            var pendingOrders = await _context.Orders
                .CountAsync(o => o.OrderStatus == OrderStatus.Pending);
            if (pendingOrders > 0)
            {
                dashboardViewModel.Notifications.Add($"{pendingOrders} đơn hàng mới cần xử lý.");
            }
            if (dashboardViewModel.LowStockProductCount > 0)
            {
                dashboardViewModel.Notifications.Add($"{dashboardViewModel.LowStockProductCount} sản phẩm sắp hết hàng cần nhập thêm.");
            }

            var currentYear = DateTime.Now.Year;
            dashboardViewModel.RevenueSummaries = await _context.Orders
                .Where(o => o.OrderDate.Year == currentYear)
                .GroupBy(o => o.OrderDate.Month)
                .Select(g => new RevenueSummary
                {
                    Month = g.Key,
                    TotalRevenue = g.Sum(o => o.TotalPrice)
                })
                .OrderBy(g => g.Month)
                .ToListAsync();

            dashboardViewModel.CategoryRevenueSummaries = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .ThenInclude(p => p.Category)
                .Where(o => o.OrderDate.Year == currentYear)
                .SelectMany(o => o.OrderDetails)
                .GroupBy(od => od.Product.Category.Name)
                .Select(g => new CategoryRevenueSummary
                {
                    CategoryName = g.Key,
                    TotalRevenue = g.Sum(od => od.Quantity * od.Price)
                })
                .ToListAsync();

            return View(dashboardViewModel);
        }

        // Xuất báo cáo dưới dạng Excel
        public async Task<IActionResult> ExportReport()
        {
            try
            {
                const int lowStockThreshold = 10;

                var today = DateTime.Today;
                var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                var endOfWeek = startOfWeek.AddDays(7);
                var currentYear = DateTime.Now.Year;

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Báo cáo Tổng quan");

                    // Tiêu đề
                    worksheet.Cells[1, 1].Value = "Báo cáo Tổng quan Admin";
                    worksheet.Cells[1, 1, 1, 4].Merge = true;
                    worksheet.Cells[1, 1].Style.Font.Size = 16;
                    worksheet.Cells[1, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

                    worksheet.Cells[2, 1].Value = $"Ngày: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
                    worksheet.Cells[2, 1, 2, 4].Merge = true;
                    worksheet.Cells[2, 1].Style.Font.Size = 12;
                    worksheet.Cells[2, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

                    int row = 4;

                    // Thống kê tổng quan
                    worksheet.Cells[row, 1].Value = "Thống kê Tổng quan";
                    worksheet.Cells[row, 1, row, 4].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    row += 2;

                    worksheet.Cells[row, 1].Value = "Tổng số sản phẩm";
                    worksheet.Cells[row, 2].Value = await _context.Products.CountAsync(p => p.IsActive);
                    row++;
                    worksheet.Cells[row, 1].Value = "Tổng số đơn hàng";
                    worksheet.Cells[row, 2].Value = await _context.Orders.CountAsync();
                    row++;
                    worksheet.Cells[row, 1].Value = "Tổng số người dùng";
                    worksheet.Cells[row, 2].Value = await _context.Users.CountAsync();
                    row++;
                    worksheet.Cells[row, 1].Value = "Tổng số danh mục";
                    worksheet.Cells[row, 2].Value = await _context.Categories.CountAsync();
                    row++;
                    worksheet.Cells[row, 1].Value = "Tổng số khuyến mãi";
                    worksheet.Cells[row, 2].Value = await _context.Promotions.CountAsync();
                    row++;
                    worksheet.Cells[row, 1].Value = "Tổng số lượt truy cập";
                    worksheet.Cells[row, 2].Value = await _context.UserAccessLogs.CountAsync(log => log.UserId != null && (log.Url == "/" || log.Url == "/Home/Index"));
                    row++;
                    worksheet.Cells[row, 1].Value = "Tổng doanh thu hôm nay";
                    worksheet.Cells[row, 2].Value = (await _context.Orders.Where(o => o.OrderDate.Date == today).SumAsync(o => o.TotalPrice)).ToString("N0") + " VND";
                    row++;
                    worksheet.Cells[row, 1].Value = "Tổng doanh thu tuần này";
                    worksheet.Cells[row, 2].Value = (await _context.Orders.Where(o => o.OrderDate >= startOfWeek && o.OrderDate < endOfWeek).SumAsync(o => o.TotalPrice)).ToString("N0") + " VND";
                    row += 2;

                    // Danh sách tất cả sản phẩm
                    worksheet.Cells[row, 1].Value = "Danh sách tất cả sản phẩm";
                    worksheet.Cells[row, 1, row, 5].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    row++;

                    var allProducts = await _context.Products
                        .Include(p => p.Category)
                        .Where(p => p.IsActive)
                        .ToListAsync();
                    if (allProducts.Any())
                    {
                        worksheet.Cells[row, 1].Value = "Tên sản phẩm";
                        worksheet.Cells[row, 2].Value = "Số lượng";
                        worksheet.Cells[row, 3].Value = "Danh mục";
                        worksheet.Cells[row, 4].Value = "Giá";
                        worksheet.Cells[row, 5].Value = "Trạng thái tồn kho";
                        worksheet.Cells[row, 1, row, 5].Style.Font.Bold = true;
                        worksheet.Cells[row, 1, row, 5].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        worksheet.Cells[row, 1, row, 5].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        row++;

                        foreach (var product in allProducts)
                        {
                            worksheet.Cells[row, 1].Value = product.Name;
                            worksheet.Cells[row, 2].Value = product.Quantity;
                            worksheet.Cells[row, 3].Value = product.Category?.Name ?? "N/A";
                            worksheet.Cells[row, 4].Value = product.Price.ToString("N0") + " VND";
                            worksheet.Cells[row, 5].Value = product.Quantity > 0 ? "Còn hàng" : "Hết hàng";
                            row++;
                        }
                        worksheet.Cells[row - allProducts.Count, 1, row - 1, 5].AutoFitColumns();
                    }
                    else
                    {
                        worksheet.Cells[row, 1].Value = "Không có sản phẩm nào.";
                        worksheet.Cells[row, 1, row, 5].Merge = true;
                    }
                    row += 2;

                    // Danh sách đơn hàng
                    worksheet.Cells[row, 1].Value = "Danh sách đơn hàng";
                    worksheet.Cells[row, 1, row, 6].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    row++;

                    var allOrders = await _context.Orders
                        .Include(o => o.User)
                        .ToListAsync();
                    if (allOrders.Any())
                    {
                        worksheet.Cells[row, 1].Value = "Mã đơn hàng";
                        worksheet.Cells[row, 2].Value = "Người dùng";
                        worksheet.Cells[row, 3].Value = "Ngày đặt";
                        worksheet.Cells[row, 4].Value = "Tổng giá";
                        worksheet.Cells[row, 5].Value = "Trạng thái";
                        worksheet.Cells[row, 6].Value = "Số lượng sản phẩm";
                        worksheet.Cells[row, 1, row, 6].Style.Font.Bold = true;
                        worksheet.Cells[row, 1, row, 6].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        worksheet.Cells[row, 1, row, 6].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        row++;

                        foreach (var order in allOrders)
                        {
                            worksheet.Cells[row, 1].Value = order.Id;
                            worksheet.Cells[row, 2].Value = order.User?.UserName ?? "N/A";
                            worksheet.Cells[row, 3].Value = order.OrderDate.ToString("dd/MM/yyyy HH:mm");
                            worksheet.Cells[row, 4].Value = order.TotalPrice.ToString("N0") + " VND";
                            worksheet.Cells[row, 5].Value = order.OrderStatus.ToString();
                            worksheet.Cells[row, 6].Value = (await _context.OrderDetails.CountAsync(od => od.OrderId == order.Id));
                            row++;
                        }
                        worksheet.Cells[row - allOrders.Count, 1, row - 1, 6].AutoFitColumns();
                    }
                    else
                    {
                        worksheet.Cells[row, 1].Value = "Không có đơn hàng nào.";
                        worksheet.Cells[row, 1, row, 6].Merge = true;
                    }
                    row += 2;

                    // Doanh thu theo tháng
                    worksheet.Cells[row, 1].Value = "Doanh thu năm " + currentYear + " (Theo tháng)";
                    worksheet.Cells[row, 1, row, 4].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    row++;

                    var revenueSummaries = await _context.Orders
                        .Where(o => o.OrderDate.Year == currentYear)
                        .GroupBy(o => o.OrderDate.Month)
                        .Select(g => new { Month = g.Key, TotalRevenue = g.Sum(o => o.TotalPrice) })
                        .OrderBy(g => g.Month)
                        .ToListAsync();
                    if (revenueSummaries.Any())
                    {
                        worksheet.Cells[row, 1].Value = "Tháng";
                        worksheet.Cells[row, 2].Value = "Doanh thu";
                        worksheet.Cells[row, 1, row, 2].Style.Font.Bold = true;
                        worksheet.Cells[row, 1, row, 2].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        worksheet.Cells[row, 1, row, 2].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        row++;

                        foreach (var summary in revenueSummaries)
                        {
                            worksheet.Cells[row, 1].Value = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(summary.Month);
                            worksheet.Cells[row, 2].Value = summary.TotalRevenue.ToString("N0") + " VND";
                            row++;
                        }
                        worksheet.Cells[row - revenueSummaries.Count, 1, row - 1, 2].AutoFitColumns();
                    }
                    else
                    {
                        worksheet.Cells[row, 1].Value = "Không có dữ liệu doanh thu.";
                        worksheet.Cells[row, 1, row, 4].Merge = true;
                    }
                    row += 2;

                    // Doanh thu theo danh mục
                    worksheet.Cells[row, 1].Value = "Doanh thu năm " + currentYear + " (Theo danh mục)";
                    worksheet.Cells[row, 1, row, 4].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    row++;

                    var categoryRevenueSummaries = await _context.Orders
                        .Include(o => o.OrderDetails)
                        .ThenInclude(od => od.Product)
                        .ThenInclude(p => p.Category)
                        .Where(o => o.OrderDate.Year == currentYear)
                        .SelectMany(o => o.OrderDetails)
                        .GroupBy(od => od.Product.Category.Name)
                        .Select(g => new { CategoryName = g.Key, TotalRevenue = g.Sum(od => od.Quantity * od.Price) })
                        .ToListAsync();
                    if (categoryRevenueSummaries.Any())
                    {
                        worksheet.Cells[row, 1].Value = "Danh mục";
                        worksheet.Cells[row, 2].Value = "Doanh thu";
                        worksheet.Cells[row, 1, row, 2].Style.Font.Bold = true;
                        worksheet.Cells[row, 1, row, 2].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        worksheet.Cells[row, 1, row, 2].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        row++;

                        foreach (var summary in categoryRevenueSummaries)
                        {
                            worksheet.Cells[row, 1].Value = summary.CategoryName ?? "N/A";
                            worksheet.Cells[row, 2].Value = summary.TotalRevenue.ToString("N0") + " VND";
                            row++;
                        }
                        worksheet.Cells[row - categoryRevenueSummaries.Count, 1, row - 1, 2].AutoFitColumns();
                    }
                    else
                    {
                        worksheet.Cells[row, 1].Value = "Không có dữ liệu doanh thu theo danh mục.";
                        worksheet.Cells[row, 1, row, 4].Merge = true;
                    }
                    row += 2;

                    // Danh sách nhà cung cấp
                    worksheet.Cells[row, 1].Value = "Danh sách nhà cung cấp";
                    worksheet.Cells[row, 1, row, 4].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    row++;

                    var suppliers = await _context.Suppliers.ToListAsync();
                    if (suppliers.Any())
                    {
                        worksheet.Cells[row, 1].Value = "Tên nhà cung cấp";
                        worksheet.Cells[row, 2].Value = "Email";
                        worksheet.Cells[row, 3].Value = "Số điện thoại";
                        worksheet.Cells[row, 4].Value = "Địa chỉ";
                        worksheet.Cells[row, 1, row, 4].Style.Font.Bold = true;
                        worksheet.Cells[row, 1, row, 4].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        worksheet.Cells[row, 1, row, 4].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        row++;

                        foreach (var supplier in suppliers)
                        {
                            worksheet.Cells[row, 1].Value = supplier.Name;
                            worksheet.Cells[row, 2].Value = supplier.Email;
                            worksheet.Cells[row, 3].Value = supplier.Phone;
                            worksheet.Cells[row, 4].Value = supplier.Address;
                            row++;
                        }
                        worksheet.Cells[row - suppliers.Count, 1, row - 1, 4].AutoFitColumns();
                    }
                    else
                    {
                        worksheet.Cells[row, 1].Value = "Không có nhà cung cấp nào.";
                        worksheet.Cells[row, 1, row, 4].Merge = true;
                    }

                    // Xuất file Excel
                    var stream = new MemoryStream(package.GetAsByteArray());
                    return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "BaoCaoTongQuan_ChiTiet.xlsx");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xuất báo cáo Excel tại: {Message}", ex.Message);
                return StatusCode(500, "Có lỗi xảy ra khi xuất báo cáo. Vui lòng thử lại sau.");
            }
        }
    }
}
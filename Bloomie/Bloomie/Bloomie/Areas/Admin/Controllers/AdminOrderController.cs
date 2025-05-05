using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Models;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminOrderController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminOrderController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. Hiển thị danh sách đơn hàng
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var orders = await _context.Orders
                .Include(o => o.User) // Sử dụng đúng tên navigation property 'User'
                .Include(o => o.OrderDetails) // Sử dụng đúng tên navigation property 'OrderDetails'
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            return View(orders);
        }

        // 2. Xem chi tiết đơn hàng
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .Include(o => o.User) // Sử dụng đúng tên navigation property 'User'
                .Include(o => o.OrderDetails) // Sử dụng đúng tên navigation property 'OrderDetails'
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }
    }
}
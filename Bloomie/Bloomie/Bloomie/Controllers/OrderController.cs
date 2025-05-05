using Bloomie.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Models;

namespace WebsiteBanHang.Controllers
{
    [Authorize] // Yêu cầu đăng nhập
    public class OrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public OrderController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // Hiển thị danh sách đơn hàng của người dùng
        public async Task<IActionResult> MyOrders()
        {
            var userId = _userManager.GetUserId(User);

            var orders = await _context.Orders
                .Where(o => o.UserId == userId)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            return View(orders);
        }

        // Hiển thị chi tiết đơn hàng
        public async Task<IActionResult> Details(int orderId)
        {
            var userId = _userManager.GetUserId(User);

            var order = await _context.Orders
                .Where(o => o.Id == orderId && o.UserId == userId) // Chỉ xem được đơn hàng của chính mình
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        // Xóa đơn hàng
        [HttpPost]
        public async Task<IActionResult> Delete(int orderId)
        {
            var userId = _userManager.GetUserId(User);

            // Tìm đơn hàng theo ID và đảm bảo nó thuộc về người dùng hiện tại
            var order = await _context.Orders
                .Where(o => o.Id == orderId && o.UserId == userId)
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                return NotFound();
            }

            // Xóa các chi tiết đơn hàng trước (nếu có)
            _context.OrderDetails.RemoveRange(order.OrderDetails);

            // Xóa đơn hàng
            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();

            // Quay về trang danh sách đơn hàng
            return RedirectToAction("MyOrders");
        }

    }
}

using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Bloomie.Extensions;

namespace Bloomie.Controllers
{
    public class ShoppingCartController : Controller
    {
        private readonly IProductService _productService;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ShoppingCartController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IProductService productService)
        {
            _productService = productService;
            _context = context;
            _userManager = userManager;
        }

        // Phương thức tiện ích để lấy hoặc tạo cartKey
        private string GetCartKey()
        {
            var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;
            string cartKey;

            if (isAuthenticated)
            {
                cartKey = $"Cart_{_userManager.GetUserId(User)}";
            }
            else
            {
                if (string.IsNullOrEmpty(HttpContext.Session.Id))
                {
                    HttpContext.Session.SetString("TempSessionId", Guid.NewGuid().ToString());
                }
                cartKey = $"Cart_Anonymous_{HttpContext.Session.GetString("TempSessionId") ?? HttpContext.Session.Id}";
            }

            return cartKey;
        }

        // Phương thức tiện ích để lấy giỏ hàng
        private ShoppingCart GetCart()
        {
            var cartKey = GetCartKey();
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(cartKey) ?? new ShoppingCart();
            UpdateCartCount(cart);
            return cart;
        }

        // Cập nhật ViewData["CartCount"]
        private void UpdateCartCount(ShoppingCart cart)
        {
            ViewData["CartCount"] = cart.TotalItems;
        }

        [HttpGet]
        public async Task<IActionResult> AddToCart(int productId, int quantity = 1, decimal? discountedPrice = null)
        {
            var product = await _productService.GetProductByIdAsync(productId);

            if (product == null)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Sản phẩm không tồn tại!" });
                }
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);
            var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;

            // Kiểm tra nếu user là Admin
            if (isAuthenticated && await _userManager.IsInRoleAsync(user, "Admin"))
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Admin không thể thêm sản phẩm vào giỏ hàng!" });
                }
                TempData["ErrorMessage"] = "🚫 Admin không thể thêm sản phẩm vào giỏ hàng.";
                return RedirectToAction("Index");
            }

            // Sử dụng discountedPrice từ client nếu có, nếu không thì dùng giá gốc
            decimal finalPrice = discountedPrice ?? product.Price;

            var cartItem = new CartItem
            {
                ProductId = productId,
                Name = product.Name,
                Price = finalPrice,
                Quantity = quantity,
                ImageUrl = product.ImageUrl
            };

            var cart = GetCart();
            cart.AddItem(cartItem);
            HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, cartCount = cart.TotalItems });
            }

            return RedirectToAction("Index");
        }

        public IActionResult Index()
        {
            var cart = GetCart();
            return View(cart);
        }

        public IActionResult RemoveFromCart(int productId)
        {
            var cart = GetCart();
            cart.RemoveItem(productId);
            HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            return RedirectToAction("Index");
        }

        public IActionResult IncreaseQuantity(int productId)
        {
            var cart = GetCart();
            var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);

            if (item != null)
            {
                item.Quantity++;
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            }

            return RedirectToAction("Index");
        }

        public IActionResult DecreaseQuantity(int productId)
        {
            var cart = GetCart();
            var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);

            if (item != null)
            {
                if (item.Quantity > 1)
                {
                    item.Quantity--;
                }
                else
                {
                    cart.Items.Remove(item);
                }
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult UpdateQuantity(int productId, int quantity)
        {
            var cart = GetCart();
            var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);

            if (item == null)
            {
                return Json(new { success = false, message = "Sản phẩm không tồn tại trong giỏ hàng!" });
            }

            if (quantity < 1)
            {
                quantity = 1;
            }

            item.Quantity = quantity;
            HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);

            var newTotal = cart.Items.Sum(i => i.Price * i.Quantity);
            return Json(new { success = true, newTotal = newTotal });
        }

        public IActionResult Checkout()
        {
            var cart = GetCart();

            if (!User.Identity.IsAuthenticated)
            {
                TempData["WarningMessage"] = "Vui lòng đăng nhập hoặc đăng ký để hoàn tất thanh toán!";
                return RedirectToAction("Login", "Account");
            }

            return View(new Order());
        }

        [HttpPost]
        public async Task<IActionResult> Checkout(Order order)
        {
            if (!User.Identity.IsAuthenticated)
            {
                TempData["WarningMessage"] = "Vui lòng đăng nhập để hoàn tất thanh toán!";
                return RedirectToAction("Login", "Account");
            }

            var cart = GetCart();

            if (!cart.Items.Any())
            {
                TempData["ErrorMessage"] = "Giỏ hàng của bạn trống!";
                return RedirectToAction("Index");
            }

            // Kiểm tra số lượng trong kho
            foreach (var item in cart.Items)
            {
                var product = await _productService.GetProductByIdAsync(item.ProductId);
                if (product == null)
                {
                    TempData["ErrorMessage"] = $"Sản phẩm {item.Name} không tồn tại!";
                    return RedirectToAction("Index");
                }

                if (product.Quantity < item.Quantity)
                {
                    TempData["ErrorMessage"] = $"Sản phẩm {item.Name} không đủ số lượng trong kho! Chỉ còn {product.Quantity} sản phẩm.";
                    return RedirectToAction("Index");
                }
            }

            // Giảm số lượng trong kho và tăng QuantitySold
            foreach (var item in cart.Items)
            {
                var product = await _productService.GetProductByIdAsync(item.ProductId);
                if (product != null)
                {
                    product.Quantity -= item.Quantity;
                    product.QuantitySold += item.Quantity;
                    await _productService.UpdateProductAsync(product);
                }
            }

            var user = await _userManager.GetUserAsync(User);
            order.UserId = user.Id;
            order.OrderDate = DateTime.Now;
            order.TotalPrice = cart.Items.Sum(i => i.Price * i.Quantity);
            order.OrderDetails = cart.Items.Select(i => new OrderDetail
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                Price = i.Price 
            }).ToList();

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Xóa giỏ hàng sau khi đặt hàng thành công
            HttpContext.Session.Remove(GetCartKey());
            ViewData["CartCount"] = 0;

            return View("OrderCompleted", order);
        }
    }
}
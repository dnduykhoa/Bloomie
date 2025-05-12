using Bloomie.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Models;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloomie.Extensions;
using Bloomie.ViewModels;
using System.Security.Claims;
using Bloomie.Models.Vnpay;
using Bloomie.Services.Implementations;
using Newtonsoft.Json;

namespace Bloomie.Controllers
{
    [Authorize]
    public class OrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPaymentService _paymentService;
        private readonly IProductService _productService;
        private readonly IMomoService _momoService;
        private readonly IVnPayService _vnPayService;
        private readonly IEmailService _emailService;
        private readonly HttpClient _httpClient;

        public OrderController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IPaymentService paymentService,
            IProductService productService,
            IEmailService emailService,
            IMomoService momoService,
            IVnPayService vnPayService)
        {
            _context = context;
            _userManager = userManager;
            _paymentService = paymentService;
            _productService = productService;
            _emailService = emailService;
            _momoService = momoService;
            _httpClient = new HttpClient();
            _vnPayService = vnPayService;
        }

        [HttpGet]
        public IActionResult Checkout()
        {
            var userId = _userManager.GetUserId(User);
            var cartKey = $"Cart_{userId}";
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(cartKey);

            if (cart == null || !cart.Items.Any())
            {
                TempData["ErrorMessage"] = "Giỏ hàng của bạn trống!";
                return RedirectToAction("Index", "ShoppingCart");
            }

            var shippingPriceCookie = Request.Cookies["ShippingPrice"];
            decimal shippingPrice = 50000; // Mặc định 50,000 đ nếu cookie không tồn tại

            if (shippingPriceCookie != null)
            {
                try
                {
                    shippingPrice = JsonConvert.DeserializeObject<decimal>(shippingPriceCookie);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deserializing shipping price cookie: {ex.Message}");
                    shippingPrice = 50000; // Nếu có lỗi khi deserialize, dùng giá mặc định
                }
            }

            var model = new CheckoutViewModel
            {
                CartItems = cart.Items,
                TotalPrice = cart.Items.Sum(item => item.Price * item.Quantity),
                ShippingCost = shippingPrice
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Checkout(CheckoutViewModel model)
        {
            Console.WriteLine($"Checkout: PaymentMethod={model.PaymentMethod}");

            var userId = _userManager.GetUserId(User);
            var cartKey = $"Cart_{userId}";
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(cartKey);

            if (cart == null || !cart.Items.Any())
            {
                TempData["ErrorMessage"] = "Giỏ hàng của bạn trống!";
                return RedirectToAction("Index", "ShoppingCart");
            }

            model.CartItems = cart.Items;

            foreach (var item in model.CartItems.ToList())
            {
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product == null || item.Quantity <= 0 || item.Quantity > product.Quantity || item.Price < 0)
                {
                    TempData["ErrorMessage"] = "Có lỗi với giỏ hàng: sản phẩm không tồn tại, số lượng hoặc giá không hợp lệ.";
                    model.TotalPrice = cart.Items.Sum(i => i.Price * i.Quantity);
                    model.CartItems = cart.Items;
                    return View(model);
                }
            }

            decimal totalPrice = model.CartItems.Sum(item => item.Price * item.Quantity);
            decimal shippingPrice = model.ShippingCost; // Lấy phí vận chuyển từ model
            totalPrice += shippingPrice; // Cộng phí vận chuyển vào tổng giá
            Console.WriteLine($"Calculated TotalPrice: {totalPrice} (including Shipping: {shippingPrice})");
            if (totalPrice <= 0)
            {
                TempData["ErrorMessage"] = "Có lỗi với giỏ hàng: Tổng tiền không hợp lệ.";
                model.TotalPrice = totalPrice - shippingPrice; // Trừ lại phí vận chuyển để hiển thị
                model.CartItems = cart.Items;
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                model.TotalPrice = totalPrice - shippingPrice; // Trừ lại phí vận chuyển để hiển thị
                model.CartItems = cart.Items;
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            string userFullName = user?.UserName ?? "Khách";
            if (user != null)
            {
                userFullName = user.FullName ?? user.UserName;
            }

            Order order = null;
            Payment payment = null;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var orderId = $"{DateTime.Now:yyyyMMddHHmmss}-{new Random().Next(1000, 9999)}";
                Console.WriteLine($"Tạo OrderId: {orderId}");

                order = new Order
                {
                    Id = orderId,
                    UserId = userId,
                    OrderDate = DateTime.Now,
                    TotalPrice = totalPrice, // Tổng giá đã bao gồm phí vận chuyển
                    ShippingAddress = model.ShippingAddress,
                    ShippingMethod = model.ShippingMethod,
                    Notes = model.Notes,
                    OrderStatus = OrderStatus.Pending,
                    SenderName = model.SenderName,
                    SenderEmail = model.SenderEmail,
                    SenderPhoneNumber = model.SenderPhoneNumber,
                    ReceiverName = model.IsSenderReceiverSame ? model.SenderName : model.ReceiverName,
                    ReceiverPhoneNumber = model.IsSenderReceiverSame ? model.SenderPhoneNumber : model.ReceiverPhoneNumber,
                    ReceiverEmail = model.IsSenderReceiverSame ? model.SenderEmail : model.ReceiverEmail,
                    IsSenderReceiverSame = model.IsSenderReceiverSame,
                    IsAnonymousSender = model.IsAnonymousSender,
                    PhoneNumber = model.SenderPhoneNumber,
                    OrderDetails = model.CartItems.Select(item => new OrderDetail
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        Price = item.Price
                    }).ToList()
                };

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();
                Console.WriteLine($"Đã lưu đơn hàng với OrderId: {order.Id}");

                foreach (var item in model.CartItems)
                {
                    var product = await _context.Products.FindAsync(item.ProductId);
                    if (product != null)
                    {
                        if (product.Quantity < item.Quantity)
                            throw new Exception($"Sản phẩm {item.Name} không đủ số lượng trong kho.");
                        product.Quantity -= item.Quantity;
                        product.QuantitySold += item.Quantity;
                        _context.Products.Update(product);
                    }
                }
                await _context.SaveChangesAsync();

                payment = await _paymentService.CreatePaymentAsync(order.Id, model.PaymentMethod, order.TotalPrice);

                HttpContext.Session.Remove(cartKey);
                ViewData["CartCount"] = 0;

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = $"Có lỗi xảy ra khi xử lý đơn hàng: {ex.Message}. Vui lòng thử lại.";
                model.TotalPrice = totalPrice - shippingPrice; // Trừ lại phí vận chuyển để hiển thị
                model.CartItems = cart.Items;
                return View(model);
            }

            try
            {
                if (model.PaymentMethod == "Momo")
                {
                    Console.WriteLine($"Gọi CallMomoPaymentApi: PaymentId={payment.Id}, Amount={order.TotalPrice}, OrderId={order.Id}");
                    string paymentUrl = await CallMomoPaymentApi(payment.Id, order.TotalPrice, order.Id, userFullName);
                    return Redirect(paymentUrl);
                }
                else if (model.PaymentMethod == "Vnpay")
                {
                    var vnpayModel = new PaymentInformationModel
                    {
                        Name = userFullName,
                        Amount = order.TotalPrice,
                        OrderDescription = $"Thanh toán đơn hàng #{order.Id} tại Bloomie",
                        OrderType = "billpayment",
                        TxnRef = order.Id
                    };
                    Console.WriteLine($"Chuyển hướng đến CreatePaymentVnpay - Amount: {vnpayModel.Amount}, TxnRef: {vnpayModel.TxnRef}");
                    return CreatePaymentVnpay(vnpayModel);
                }
                else if (model.PaymentMethod == "CashOnDelivery")
                {
                    order.OrderStatus = OrderStatus.Processing;
                    await _context.SaveChangesAsync();
                    await _paymentService.ProcessPaymentAsync(payment.Id, "Paid", null);
                    var userFromDb = await _userManager.GetUserAsync(User);
                    await SendOrderConfirmationEmail(userFromDb, order);
                    return RedirectToAction("OrderConfirmation", new { orderId = order.Id });
                }
                else
                {
                    TempData["ErrorMessage"] = "Phương thức thanh toán không hợp lệ.";
                    return RedirectToAction("Checkout");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong xử lý thanh toán: {ex.Message}");
                TempData["ErrorMessage"] = $"Lỗi khi xử lý thanh toán: {ex.Message}. Đơn hàng đã được lưu, nhưng thanh toán không thành công.";
                return RedirectToAction("OrderConfirmation", new { orderId = order.Id });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreatePaymentMomo(OrderInfoModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    TempData["ErrorMessage"] = "Dữ liệu gửi lên không hợp lệ.";
                    return RedirectToAction("Checkout");
                }

                Console.WriteLine($"CreatePaymentMomo: OrderId={model.OrderId}, Amount={model.Amount}, OrderInformation={model.OrderInformation}, FullName={model.FullName}");

                var response = await _momoService.CreatePaymentMomo(model);
                if (response != null && response.ResultCode == 0)
                {
                    Console.WriteLine($"MoMo PayUrl: {response.PayUrl}");
                    return Redirect(response.PayUrl);
                }
                else
                {
                    TempData["ErrorMessage"] = $"Lỗi MoMo: {response?.Message ?? "Không có phản hồi từ MoMo"}";
                    return RedirectToAction("Checkout");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong CreatePaymentMomo: {ex.Message}");
                TempData["ErrorMessage"] = $"Có lỗi xảy ra: {ex.Message}";
                return RedirectToAction("Checkout");
            }
        }

        [HttpGet]
        public async Task<IActionResult> PaymentCallBack()
        {
            var requestQuery = HttpContext.Request.Query;
            Console.WriteLine($"PaymentCallBack - Query: {string.Join(", ", requestQuery.Select(q => $"{q.Key}: {q.Value}"))}");

            // Kiểm tra chữ ký và lấy phản hồi từ MoMo
            var response = _momoService.PaymentExecuteAsync(requestQuery);
            if (response == null || response.ResultCode == -1)
            {
                Console.WriteLine("Lỗi: Chữ ký MoMo không hợp lệ.");
                TempData["ErrorMessage"] = "Chữ ký giao dịch MoMo không hợp lệ.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            // Kiểm tra orderId
            string orderIdStr = response.OrderId;
            Console.WriteLine($"MoMo Callback: OrderId from MoMo={orderIdStr}");
            if (string.IsNullOrEmpty(orderIdStr))
            {
                Console.WriteLine("Lỗi: OrderId không hợp lệ.");
                TempData["ErrorMessage"] = "Mã đơn hàng không hợp lệ.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            // Tìm đơn hàng trong cơ sở dữ liệu
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .Where(o => o.Id == orderIdStr)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                Console.WriteLine($"Order not found for OrderId: {orderIdStr}");
                TempData["ErrorMessage"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            try
            {
                if (response.ResultCode == 0) // Thanh toán thành công
                {
                    Console.WriteLine($"MoMo Payment Success - OrderId: {orderIdStr}, Amount: {response.Amount}");
                    order.OrderStatus = OrderStatus.Processing;
                    _context.Update(order);

                    var payment = await _context.Payments
                        .Where(p => p.OrderId == orderIdStr)
                        .FirstOrDefaultAsync();
                    if (payment != null)
                    {
                        await _paymentService.ProcessPaymentAsync(payment.Id, "Paid", null);
                    }

                    var user = await _userManager.GetUserAsync(User);
                    if (user != null)
                    {
                        await SendOrderConfirmationEmail(user, order);
                    }
                }
                else // Thanh toán thất bại
                {
                    Console.WriteLine($"MoMo Payment Failed - OrderId: {orderIdStr}, ResultCode: {response.ResultCode}");
                    order.OrderStatus = OrderStatus.Cancelled;
                    _context.Update(order);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in PaymentCallBack: {ex.Message}");
                TempData["ErrorMessage"] = $"Có lỗi khi xử lý thanh toán: {ex.Message}";
                return RedirectToAction("Index", "ShoppingCart");
            }

            return RedirectToAction("OrderConfirmation", new { orderId = orderIdStr });
        }

        public IActionResult CreatePaymentVnpay(PaymentInformationModel model)
        {
            Console.WriteLine($"CreatePaymentVnpay - Name: {model.Name}, Amount: {model.Amount}, OrderDescription: {model.OrderDescription}, OrderType: {model.OrderType}, TxnRef: {model.TxnRef}");
            if (model.Amount <= 0)
            {
                Console.WriteLine("Lỗi: Số tiền không hợp lệ");
                TempData["ErrorMessage"] = "Số tiền thanh toán không hợp lệ.";
                return RedirectToAction("Checkout");
            }
            if (string.IsNullOrEmpty(model.OrderDescription))
            {
                Console.WriteLine("Lỗi: OrderDescription không được để trống");
                TempData["ErrorMessage"] = "Mô tả đơn hàng không được để trống.";
                return RedirectToAction("Checkout");
            }
            if (string.IsNullOrEmpty(model.TxnRef))
            {
                Console.WriteLine("Lỗi: TxnRef không được để trống");
                TempData["ErrorMessage"] = "Mã giao dịch không được để trống.";
                return RedirectToAction("Checkout");
            }

            try
            {
                var url = _vnPayService.CreatePaymentVnpay(model, HttpContext);
                Console.WriteLine($"Generated VnPay URL: {url}");
                return Redirect(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong CreatePaymentVnpay: {ex.Message}");
                TempData["ErrorMessage"] = $"Có lỗi khi tạo thanh toán VnPay: {ex.Message}";
                return RedirectToAction("Checkout");
            }
        }

        [HttpGet]
        public async Task<IActionResult> PaymentCallbackVnpay()
        {
            var response = _vnPayService.PaymentExecute(Request.Query);
            Console.WriteLine($"PaymentCallbackVnpay - Success: {response.Success}, VnPayResponseCode: {response.VnPayResponseCode}, OrderId: {response.OrderId}");

            if (string.IsNullOrEmpty(response.OrderId))
            {
                TempData["ErrorMessage"] = "Mã đơn hàng không hợp lệ từ VnPay.";
                return RedirectToAction("Checkout");
            }

            // Sử dụng response.OrderId (được lấy từ vnp_TxnRef) để tìm đơn hàng
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .Where(o => o.Id == response.OrderId)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                Console.WriteLine($"Order not found for OrderId: {response.OrderId}");
                TempData["ErrorMessage"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Checkout");
            }

            try
            {
                if (response.Success)
                {
                    Console.WriteLine($"VnPay Payment Success - OrderId: {response.OrderId}");
                    order.OrderStatus = OrderStatus.Processing;
                    _context.Update(order);

                    var payment = await _context.Payments
                        .Where(p => p.OrderId == response.OrderId)
                        .FirstOrDefaultAsync();
                    if (payment != null)
                    {
                        await _paymentService.ProcessPaymentAsync(payment.Id, "Paid", null);
                    }

                    var user = await _userManager.GetUserAsync(User);
                    if (user != null)
                    {
                        await SendOrderConfirmationEmail(user, order);
                    }
                    await _context.SaveChangesAsync();
                }
                else
                {
                    Console.WriteLine($"VnPay Payment Failed - OrderId: {response.OrderId}, ResponseCode: {response.VnPayResponseCode}");
                    order.OrderStatus = OrderStatus.Cancelled;
                    _context.Update(order);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in PaymentCallbackVnpay: {ex.Message}");
                TempData["ErrorMessage"] = $"Có lỗi khi xử lý thanh toán: {ex.Message}";
                return RedirectToAction("Checkout");
            }

            return RedirectToAction("OrderConfirmation", new { orderId = response.OrderId });
        }

        //[HttpPost]
        //public IActionResult CreatePaymentVnpay(PaymentInformationModel model)
        //{
        //    var url = _vnPayService.CreatePaymentVnpay(model, HttpContext);

        //    return Redirect(url);
        //}
        //[HttpGet]
        //public IActionResult PaymentCallbackVnpay()
        //{
        //    var response = _vnPayService.PaymentExecute(Request.Query);

        //    return Json(response);
        //}


        //[HttpGet]
        //public async Task<IActionResult> PaymentCallBack()
        //{
        //    var requestQuery = HttpContext.Request.Query;
        //    string resultCode = requestQuery["resultCode"].ToString();

        //    // Lấy thông tin người dùng từ UserManager
        //    var user = await _userManager.GetUserAsync(User);
        //    string userFullName = user?.UserName ?? "Khách";
        //    if (user != null && !string.IsNullOrEmpty(user.FullName))
        //    {
        //        userFullName = user.FullName;
        //    }

        //    if (resultCode == "0") // Giao dịch thành công
        //    {
        //        var newMomoInsert = new MomoInfoModel
        //        {
        //            OrderId = requestQuery["orderId"],
        //            FullName = userFullName,
        //            Amount = decimal.Parse(requestQuery["amount"]),
        //            OrderInfo = requestQuery["orderInfo"],
        //            DatePaid = DateTime.Now,
        //        };
        //        _context.Add(newMomoInsert);
        //        await _context.SaveChangesAsync();

        //        TempData["success"] = "Thanh toán MOMO thành công.";
        //        // Chuyển hướng đến OrderConfirmation với orderId
        //        return RedirectToAction("OrderConfirmation", new { orderId = requestQuery["orderId"] });
        //    }
        //    else // Giao dịch thất bại hoặc bị hủy
        //    {
        //        TempData["success"] = "Giao dịch MOMO đã bị hủy hoặc thất bại.";
        //        return RedirectToAction("Index", "ShoppingCart");
        //    }
        //}

        [HttpPost]
        public async Task<IActionResult> GetShipping(Shipping shippingModel, string quan, string tinh, string phuong)
        {
            var existingShipping = await _context.Shippings
                .FirstOrDefaultAsync(x => x.City == tinh && x.District == quan && x.Ward == phuong);

            decimal shippingPrice = 0;

            if (existingShipping != null)
            {
                shippingPrice = existingShipping.Price;
            }
            else
            {
                shippingPrice = 50000;
            }
            var shippingPriceJson = JsonConvert.SerializeObject(shippingPrice);
            try
            {
                var cookieOptions = new CookieOptions
                {
                    HttpOnly = true,
                    Expires = DateTimeOffset.UtcNow.AddMinutes(30),
                    Secure = true
                };

                Response.Cookies.Append("ShippingPrice", shippingPriceJson, cookieOptions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding shipping price cookie: {ex.Message}");
            }
            return Json(new { shippingPrice });
        }

        public async Task<IActionResult> History()
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

        public async Task<IActionResult> Details(string orderId)
        {
            var userId = _userManager.GetUserId(User);

            var order = await _context.Orders
                .Where(o => o.Id == orderId && o.UserId == userId)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                return NotFound();
            }

            // Lấy thông tin thanh toán liên quan đến đơn hàng
            var payment = await _context.Payments
                .Where(p => p.OrderId == orderId)
                .FirstOrDefaultAsync();

            // Thêm thông tin thanh toán vào ViewData hoặc ViewBag để sử dụng trong view
            ViewBag.PaymentMethod = payment?.PaymentMethod ?? "Không xác định";
            ViewBag.PaymentStatus = payment?.PaymentStatus ?? "Không xác định";

            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string orderId) // Đổi từ int sang string
        {
            var userId = _userManager.GetUserId(User);

            var order = await _context.Orders
                .Where(o => o.Id == orderId && o.UserId == userId)
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                return NotFound();
            }

            _context.OrderDetails.RemoveRange(order.OrderDetails);
            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();

            return RedirectToAction("History");
        }

        public async Task<IActionResult> OrderConfirmation(string orderId)
        {
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(orderId))
            {
                Console.WriteLine("OrderId is null or empty");
                TempData["ErrorMessage"] = "Mã đơn hàng không hợp lệ.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            var order = await _context.Orders
                .Where(o => o.Id == orderId && o.UserId == userId)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                Console.WriteLine($"Order not found for OrderId: {orderId} and UserId: {userId}");
                TempData["ErrorMessage"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            return View(order);
        }

        //public async Task<IActionResult> OrderConfirmation(string orderId)
        //{
        //    var userId = _userManager.GetUserId(User);

        //    if (string.IsNullOrEmpty(orderId) || !long.TryParse(orderId, out long orderIdLong))
        //    {
        //        TempData["ErrorMessage"] = "Mã đơn hàng không hợp lệ.";
        //        return RedirectToAction("Index", "ShoppingCart");
        //    }

        //    var order = await _context.Orders
        //        .Where(o => o.Id == orderIdLong && o.UserId == userId)
        //        .Include(o => o.OrderDetails)
        //        .ThenInclude(od => od.Product)
        //        .FirstOrDefaultAsync();

        //    if (order == null)
        //    {
        //        TempData["ErrorMessage"] = "Không tìm thấy đơn hàng.";
        //        return RedirectToAction("Index", "ShoppingCart");
        //    }

        //    return View(order);
        //}

        private async Task SendOrderConfirmationEmail(ApplicationUser user, Order order)
        {
            var subjectForSender = $"Xác Nhận Đơn Hàng #{order.Id}";
            var messageForSender = new StringBuilder();

            messageForSender.AppendLine("<!DOCTYPE html>");
            messageForSender.AppendLine("<html lang='vi'>");
            messageForSender.AppendLine("<head>");
            messageForSender.AppendLine("<meta charset='UTF-8'>");
            messageForSender.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            messageForSender.AppendLine("<title>Xác Nhận Đơn Hàng</title>");
            messageForSender.AppendLine("<style>");
            messageForSender.AppendLine("body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; }");
            messageForSender.AppendLine(".container { max-width: 600px; margin: 20px auto; padding: 20px; border: 1px solid #ddd; border-radius: 8px; background-color: #f9f9f9; }");
            messageForSender.AppendLine(".header { text-align: center; background-color: #ff6f61; color: white; padding: 15px; border-radius: 8px 8px 0 0; }");
            messageForSender.AppendLine(".content { padding: 20px; }");
            messageForSender.AppendLine(".footer { text-align: center; padding: 10px; border-top: 1px solid #ddd; margin-top: 20px; font-size: 12px; color: #777; }");
            messageForSender.AppendLine("h2 { color: #ff6f61; }");
            messageForSender.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 10px; }");
            messageForSender.AppendLine("table, th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            messageForSender.AppendLine("th { background-color: #f2f2f2; }");
            messageForSender.AppendLine(".button { display: inline-block; padding: 10px 20px; background-color: #ff6f61; color: white; text-decoration: none; border-radius: 5px; margin-top: 15px; }");
            messageForSender.AppendLine("</style>");
            messageForSender.AppendLine("</head>");
            messageForSender.AppendLine("<body>");

            messageForSender.AppendLine("<div class='container'>");
            messageForSender.AppendLine("<div class='header'>");
            messageForSender.AppendLine("<h1>Bloomie Shop</h1>");
            messageForSender.AppendLine("</div>");
            messageForSender.AppendLine("<div class='content'>");
            messageForSender.AppendLine($"<h2>Xác Nhận Đơn Hàng #{order.Id}</h2>");
            messageForSender.AppendLine($"<p>Xin chào <strong>{order.SenderName}</strong>,</p>");
            messageForSender.AppendLine("<p>Cảm ơn bạn đã mua sắm tại Bloomie Shop! Đơn hàng của bạn đã được đặt thành công. Dưới đây là thông tin chi tiết:</p>");

            messageForSender.AppendLine("<h3>Thông Tin Người Gửi & Người Nhận</h3>");
            messageForSender.AppendLine("<table>");
            messageForSender.AppendLine($"<tr><th>Người Gửi</th><td>{order.SenderName}<br>Email: {order.SenderEmail}<br>Số điện thoại: {order.SenderPhoneNumber}</td></tr>");
            messageForSender.AppendLine($"<tr><th>Người Nhận</th><td>{order.ReceiverName}<br>Số điện thoại: {order.ReceiverPhoneNumber}</td></tr>");
            messageForSender.AppendLine("</table>");

            messageForSender.AppendLine("<h3>Thông Tin Đơn Hàng</h3>");
            messageForSender.AppendLine("<table>");
            messageForSender.AppendLine($"<tr><th>Ngày Đặt Hàng</th><td>{order.OrderDate:dd/MM/yyyy HH:mm}</td></tr>");
            messageForSender.AppendLine($"<tr><th>Địa Chỉ Giao Hàng</th><td>{order.ShippingAddress}</td></tr>");
            messageForSender.AppendLine($"<tr><th>Phương Thức Giao Hàng</th><td>{order.ShippingMethod}</td></tr>");
            messageForSender.AppendLine($"<tr><th>Ghi Chú</th><td>{(string.IsNullOrEmpty(order.Notes) ? "Không có" : order.Notes)}</td></tr>");
            messageForSender.AppendLine($"<tr><th>Tổng Tiền</th><td>{order.TotalPrice:#,##0} đ</td></tr>");
            messageForSender.AppendLine("</table>");

            messageForSender.AppendLine("<h3>Chi Tiết Đơn Hàng</h3>");
            messageForSender.AppendLine("<table>");
            messageForSender.AppendLine("<tr><th>Sản Phẩm</th><th>Số Lượng</th><th>Giá</th><th>Tổng</th></tr>");
            foreach (var detail in order.OrderDetails)
            {
                messageForSender.AppendLine($"<tr><td>{detail.Product.Name}</td><td>{detail.Quantity}</td><td>{detail.Price:#,##0} đ</td><td>{(detail.Price * detail.Quantity):#,##0} đ</td></tr>");
            }
            messageForSender.AppendLine("</table>");

            messageForSender.AppendLine($"<a href='https://yourdomain.com/Order/Details?orderId={order.Id}' class='button'>Xem Chi Tiết Đơn Hàng</a>");
            messageForSender.AppendLine("<p>Chúng tôi sẽ xử lý đơn hàng của bạn sớm nhất có thể. Nếu bạn có bất kỳ câu hỏi nào, vui lòng liên hệ với chúng tôi qua email <a href='mailto:bloomieshop@gmail.com'>bloomieshop@gmail.com</a> hoặc số điện thoại 0123 456 789.</p>");
            messageForSender.AppendLine("</div>");
            messageForSender.AppendLine("<div class='footer'>");
            messageForSender.AppendLine("© 2025 Bloomie Shop. Tất cả quyền được bảo lưu.");
            messageForSender.AppendLine("</div>");
            messageForSender.AppendLine("</div>");
            messageForSender.AppendLine("</body>");
            messageForSender.AppendLine("</html>");

            await _emailService.SendEmailAsync(order.SenderEmail, subjectForSender, messageForSender.ToString());
        }

        private async Task<string> CallMomoPaymentApi(int paymentId, decimal amount, string orderId, string userFullName)
        {
            if (amount <= 0)
            {
                Console.WriteLine("Lỗi: Số tiền thanh toán không hợp lệ.");
                throw new ArgumentException("Số tiền thanh toán không hợp lệ.");
            }

            if (_momoService == null)
            {
                Console.WriteLine("Lỗi: IMomoService không được khởi tạo.");
                throw new InvalidOperationException("IMomoService không được khởi tạo.");
            }

            var orderInfoModel = new OrderInfoModel
            {
                OrderId = orderId,
                Amount = (long)amount,
                OrderInformation = $"Thanh toán đơn hàng #{orderId} tại Bloomie",
                FullName = userFullName
            };

            Console.WriteLine($"CallMomoPaymentApi: OrderId={orderInfoModel.OrderId}, Amount={orderInfoModel.Amount}, OrderInformation={orderInfoModel.OrderInformation}, FullName={orderInfoModel.FullName}");

            try
            {
                var response = await _momoService.CreatePaymentMomo(orderInfoModel);
                Console.WriteLine($"MoMo Response: ResultCode={response?.ResultCode}, PayUrl={response?.PayUrl}, Message={response?.Message}, RawResponse={Newtonsoft.Json.JsonConvert.SerializeObject(response)}");

                if (response != null && response.ResultCode == 0 && !string.IsNullOrEmpty(response.PayUrl))
                {
                    Console.WriteLine($"Thành công: Chuyển hướng đến PayUrl: {response.PayUrl}");
                    return response.PayUrl;
                }
                else
                {
                    Console.WriteLine($"Lỗi MoMo: ResultCode={response?.ResultCode}, Message={response?.Message}");
                    throw new Exception($"Không thể tạo thanh toán MoMo: {response?.Message ?? "Không có phản hồi hợp lệ từ MoMo"}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi chi tiết trong CallMomoPaymentApi: {ex.Message}\nStackTrace: {ex.StackTrace}");
                throw;
            }
        }

        [HttpPost]
        public async Task<IActionResult> MomoNotify()
        {
            var collection = HttpContext.Request.Query;
            var response = _momoService.PaymentExecuteAsync(collection);
            var paymentId = int.Parse(response.OrderId);
            var resultCode = collection["resultCode"].ToString();
            var status = resultCode == "0" ? "Paid" : "Failed";
            await _paymentService.ProcessPaymentAsync(paymentId, status, null);
            return Ok();
        }
    }
}
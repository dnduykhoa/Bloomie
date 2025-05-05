using System.Diagnostics;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Extensions;
using Bloomie.Models.ViewModels;

namespace Bloomie.Controllers
{
    public class HomeController : Controller
    {
        private readonly IProductService _productService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        public HomeController(IProductService productService, UserManager<ApplicationUser> userManager, ApplicationDbContext context)
        {
            _productService = productService;
            _userManager = userManager;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var allProducts = await _productService.GetAllProductsAsync();
            allProducts ??= new List<Product>();

            var activePromotions = await _context.Promotions
                .Where(p => p.IsActive && p.StartDate <= DateTime.Now && p.EndDate >= DateTime.Now)
                .Include(p => p.PromotionProducts)
                .ThenInclude(pp => pp.Product)
                .ToListAsync();

            var productIdsWithPromotions = activePromotions
                .SelectMany(p => p.PromotionProducts)
                .Select(pp => pp.ProductId)
                .Distinct()
                .ToList();

            var promotionDiscounts = new Dictionary<int, decimal>();
            foreach (var promotion in activePromotions)
            {
                foreach (var pp in promotion.PromotionProducts)
                {
                    if (!promotionDiscounts.ContainsKey(pp.ProductId))
                    {
                        promotionDiscounts[pp.ProductId] = promotion.DiscountPercentage;
                    }
                    else
                    {
                        promotionDiscounts[pp.ProductId] = Math.Max(promotionDiscounts[pp.ProductId], promotion.DiscountPercentage);
                    }
                }
            }

            var newProducts = allProducts
                .Where(p => p.CreatedDate >= DateTime.Now.AddDays(-7) || p.IsNew)
                .Take(10)
                .ToList();

            var bestSellingProducts = allProducts
                .OrderByDescending(p => p.QuantitySold)
                .Take(20)
                .ToList();

            foreach (var product in newProducts.Concat(bestSellingProducts))
            {
                product.DiscountPercentage = 0;
                if (promotionDiscounts.ContainsKey(product.Id))
                {
                    product.DiscountPercentage = promotionDiscounts[product.Id];
                }
            }

            // Lấy danh mục cha và tự động bao gồm danh mục con
            var categories = await _context.Categories
                .Where(c => c.ParentCategoryId == null)
                .Include(c => c.SubCategories)
                .OrderBy(c => c.Name)
                .ToListAsync();

            // Sắp xếp danh mục con theo bảng chữ cái
            foreach (var category in categories)
            {
                category.SubCategories = category.SubCategories.OrderBy(sc => sc.Name).ToList();
            }

            ViewBag.Categories = categories ?? new List<Category>();

            var viewModel = new ProductViewModel
            {
                NewProducts = newProducts,
                BestSellingProducts = bestSellingProducts
            };

            var userId = _userManager.GetUserId(User);
            var cartKey = $"Cart_{userId}";
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(cartKey) ?? new ShoppingCart();
            ViewData["CartCount"] = cart.TotalItems;

            return View(viewModel);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        //[HttpGet]
        //public IActionResult Contact()
        //{
        //    return View(new ContactFormModel());
        //}

        //[HttpPost]
        //public IActionResult Contact(ContactFormModel model)
        //{
        //    if (ModelState.IsValid)
        //    {
        //        TempData["SuccessMessage"] = "Cảm ơn bạn đã liên hệ! Chúng tôi sẽ phản hồi sớm nhất có thể.";
        //        return RedirectToAction("Contact");
        //    }
        //    return View(model);
        //}
    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Bloomie.Services.Implementations;

namespace WebsiteBanHang.Controllers
{
    public class ProductController : Controller
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IPromotionService _promotionService;

        public ProductController(IProductService productService, ICategoryService categoryService, IPromotionService promotionService)
        {
            _productService = productService;
            _categoryService = categoryService;
            _promotionService = promotionService;
        }

        // Hiển thị danh sách sản phẩm với bộ lọc, sắp xếp và phân trang
        [HttpGet]
        public async Task<IActionResult> Index(int? categoryId, string searchString, decimal? minPrice, decimal? maxPrice, string sortOrder, bool? isNew, int pageNumber = 1, int pageSize = 15)
        {
            // Lấy tất cả danh mục trước
            var allCategories = await _categoryService.GetAllCategoriesAsync();

            // Lấy danh mục cha
            var parentCategories = allCategories.Where(c => c.ParentCategoryId == null)
                                               .OrderBy(c => c.Name)
                                               .ToList();

            // Gán danh mục con cho từng danh mục cha
            foreach (var category in parentCategories)
            {
                category.SubCategories = allCategories.Where(c => c.ParentCategoryId == category.Id)
                                                     .OrderBy(c => c.Name)
                                                     .ToList();
            }

            ViewBag.Categories = parentCategories ?? new List<Category>();

            // Phần còn lại của mã không thay đổi
            ViewBag.CategoryId = categoryId?.ToString();
            ViewBag.SearchString = searchString;
            ViewBag.MinPrice = minPrice?.ToString();
            ViewBag.MaxPrice = maxPrice?.ToString();
            ViewBag.SortOrder = sortOrder;
            ViewBag.IsNew = isNew?.ToString(); // Thêm để hiển thị trạng thái lọc

            var products = await _productService.GetAllProductsAsync();
            products = products.Where(p => p.IsActive).ToList();
            if (products == null)
            {
                products = new List<Product>();
            }

            var currentDate = DateTime.Now;
            var promotions = await _promotionService.GetAllPromotionsAsync();
            promotions = promotions.Where(p => p.IsActive).ToList();

            foreach (var product in products)
            {
                var applicablePromotions = promotions
                    .Where(p => p.PromotionProducts.Any(pp => pp.ProductId == product.Id))
                    .ToList();

                if (applicablePromotions.Any())
                {
                    var bestPromotion = applicablePromotions.OrderByDescending(p => p.DiscountPercentage).First();
                    product.DiscountPercentage = bestPromotion.DiscountPercentage;
                }
                else
                {
                    product.DiscountPercentage = 0;
                }
            }

            // Áp dụng bộ lọc IsNew
            if (isNew.HasValue && isNew.Value)
            {
                products = products.Where(p => p.IsNew).ToList(); // Chỉ lấy sản phẩm có IsNew = true
                ViewBag.CategoryName = "Sản phẩm mới";
            }
            else if (categoryId.HasValue)
            {
                var category = await _categoryService.GetCategoryByIdAsync(categoryId.Value);
                if (category != null)
                {
                    var categoryIds = new List<int> { categoryId.Value };
                    if (category.SubCategories != null)
                    {
                        categoryIds.AddRange(category.SubCategories.Select(c => c.Id));
                    }
                    products = products.Where(p => categoryIds.Contains(p.CategoryId)).ToList();
                    ViewBag.CategoryName = category.Name;
                }
                else
                {
                    ViewBag.CategoryName = "Không tìm thấy danh mục";
                }
            }
            else
            {
                ViewBag.CategoryName = "Tất cả sản phẩm";
            }

            if (!string.IsNullOrEmpty(searchString))
            {
                products = products.Where(p => p.Name.ToLower().Contains(searchString.ToLower())).ToList();
            }

            if (minPrice.HasValue)
            {
                products = products.Where(p => (p.DiscountPercentage > 0 ? (p.DiscountPrice ?? p.Price) : p.Price) >= minPrice.Value).ToList();
            }
            if (maxPrice.HasValue)
            {
                products = products.Where(p => (p.DiscountPercentage > 0 ? (p.DiscountPrice ?? p.Price) : p.Price) <= maxPrice.Value).ToList(); // Sửa minPrice thành maxPrice
            }

            switch (sortOrder)
            {
                case "price_desc":
                    products = products.OrderByDescending(p => (p.DiscountPercentage > 0 ? (p.DiscountPrice ?? p.Price) : p.Price)).ToList();
                    break;
                case "price_asc":
                    products = products.OrderBy(p => (p.DiscountPercentage > 0 ? (p.DiscountPrice ?? p.Price) : p.Price)).ToList();
                    break;
                default:
                    break;
            }

            var totalItems = products.Count();
            products = products.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

            ViewBag.PageNumber = pageNumber;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalItems = totalItems;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            return View(products);
        }

        // Hiển thị thông tin chi tiết sản phẩm
        [HttpGet]
        public async Task<IActionResult> Display(int id)
        {
            // Lấy tất cả danh mục trước
            var allCategories = await _categoryService.GetAllCategoriesAsync();

            // Lấy danh mục cha
            var parentCategories = allCategories.Where(c => c.ParentCategoryId == null)
                                               .OrderBy(c => c.Name)
                                               .ToList();

            // Gán danh mục con cho từng danh mục cha
            foreach (var category in parentCategories)
            {
                category.SubCategories = allCategories.Where(c => c.ParentCategoryId == category.Id)
                                                     .OrderBy(c => c.Name)
                                                     .ToList();
            }

            ViewBag.Categories = parentCategories ?? new List<Category>();

            var product = await _productService.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound();
            }

            var currentDate = DateTime.Now;
            var promotions = await _promotionService.GetAllPromotionsAsync();
            var applicablePromotions = promotions
                .Where(p => p.IsActive && p.StartDate <= currentDate && p.EndDate >= currentDate)
                .Where(p => p.PromotionProducts.Any(pp => pp.ProductId == product.Id))
                .ToList();

            if (applicablePromotions.Any())
            {
                var bestPromotion = applicablePromotions.OrderByDescending(p => p.DiscountPercentage).First();
                product.DiscountPercentage = bestPromotion.DiscountPercentage;
            }
            else
            {
                product.DiscountPercentage = 0;
            }

            return View(product);
        }
    }
}

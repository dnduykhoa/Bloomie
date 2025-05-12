using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc;
using Bloomie.Areas.Admin.Models;
using Bloomie.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloomie.Data;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = Role.Role_Admin)]
    public class AdminProductController : Controller
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IInventoryService _inventoryService;
        private readonly ApplicationDbContext _context;

        public AdminProductController(
            IProductService productService,
            ICategoryService categoryService,
            IInventoryService inventoryService, ApplicationDbContext context)
        {
            _productService = productService;
            _categoryService = categoryService;
            _inventoryService = inventoryService;
            _context = context;
        }

        public async Task<IActionResult> Index(string searchString, int pageNumber = 1, int pageSize = 20)
        {
            var products = await _productService.GetAllProductsAsync();
            if (!string.IsNullOrEmpty(searchString))
            {
                products = products.Where(p => p.Name.ToLower().Contains(searchString.ToLower())).ToList();
            }

            int totalItems = products.Count();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            var pagedProducts = products
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = totalPages;
            ViewData["PageSize"] = pageSize;
            ViewData["TotalItems"] = totalItems;
            ViewData["SearchString"] = searchString;

            return View(pagedProducts);
        }

        [HttpGet]
        public async Task<IActionResult> Add(int? currentPage)
        {
            var categories = await _categoryService.GetAllCategoriesAsync();
            var flowerTypes = await _inventoryService.GetAllFlowerTypesAsync();

            ViewBag.Categories = new SelectList(categories, "Id", "Name");
            ViewBag.FlowerTypes = new SelectList(flowerTypes, "Id", "Name");
            ViewData["CurrentPage"] = currentPage ?? 1;

            return View(new CreateProductViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Add(CreateProductViewModel viewModel, IFormFile imageUrl, List<IFormFile> additionalImages, int currentPage)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (viewModel.Price <= 0)
                    {
                        ModelState.AddModelError("Price", "Giá sản phẩm phải lớn hơn 0.");
                        throw new Exception("Giá sản phẩm không hợp lệ.");
                    }

                    if (viewModel.CategoryId <= 0)
                    {
                        ModelState.AddModelError("CategoryId", "Vui lòng chọn một danh mục hợp lệ.");
                        throw new Exception("Danh mục không hợp lệ.");
                    }

                    if (!viewModel.FlowerTypes.Any())
                    {
                        ModelState.AddModelError("FlowerTypes", "Vui lòng chọn ít nhất một loại hoa.");
                        throw new Exception("Chưa chọn loại hoa nào.");
                    }

                    var product = new Product
                    {
                        Name = viewModel.Name,
                        Description = viewModel.Description,
                        Price = viewModel.Price,
                        Quantity = viewModel.Quantity,
                        Unit = viewModel.Unit,
                        IsActive = viewModel.IsActive,
                        IsNew = viewModel.IsNew,
                        CategoryId = viewModel.CategoryId,
                        CreatedDate = DateTime.Now,
                        QuantitySold = 0,
                        Images = new List<ProductImage>(),
                        FlowerTypeProducts = new List<FlowerTypeProduct>()
                    };

                    if (imageUrl != null && imageUrl.Length > 0)
                    {
                        var imagePath = await SaveImage(imageUrl);
                        product.ImageUrl = imagePath;
                    }

                    if (additionalImages != null && additionalImages.Any())
                    {
                        foreach (var image in additionalImages)
                        {
                            if (image != null && image.Length > 0)
                            {
                                var imgPath = await SaveImage(image);
                                product.Images.Add(new ProductImage { Url = imgPath });
                            }
                        }
                    }

                    foreach (var flowerType in viewModel.FlowerTypes)
                    {
                        var existingFlowerType = await _context.FlowerTypes
                            .Include(ft => ft.BatchFlowerTypes)
                            .ThenInclude(bft => bft.Batch) // Đảm bảo tải Batch
                            .FirstOrDefaultAsync(ft => ft.Id == flowerType.FlowerTypeId);
                        if (existingFlowerType == null)
                        {
                            throw new Exception($"Loại hoa với ID {flowerType.FlowerTypeId} không tồn tại.");
                        }

                        int totalFlowersNeeded = flowerType.Quantity * viewModel.Quantity;
                        if (existingFlowerType.Quantity < totalFlowersNeeded)
                        {
                            throw new Exception($"Số lượng tồn kho của {existingFlowerType.Name} không đủ. Cần {totalFlowersNeeded} bông, nhưng chỉ có {existingFlowerType.Quantity} trong kho.");
                        }

                        // Lọc các BatchFlowerType có Batch hợp lệ và ExpiryDate còn hạn
                        var batchFlowerTypes = existingFlowerType.BatchFlowerTypes?
                            .Where(bft => bft.CurrentQuantity > 0 && bft.Batch != null && bft.Batch.ExpiryDate > DateTime.Now)
                            .OrderBy(bft => bft.Batch.ExpiryDate)
                            .ToList() ?? new List<BatchFlowerType>();
                        if (!batchFlowerTypes.Any())
                        {
                            // Thêm thông tin debug để kiểm tra
                            var debugInfo = existingFlowerType.BatchFlowerTypes?
                                .Select(bft => new
                                {
                                    bft.FlowerTypeId,
                                    bft.CurrentQuantity,
                                    BatchExists = bft.Batch != null,
                                    ExpiryDate = bft.Batch?.ExpiryDate
                                })
                                .ToList();
                            throw new Exception($"Không có lô hoa nào còn hạn sử dụng cho {existingFlowerType.Name}. Debug Info: {System.Text.Json.JsonSerializer.Serialize(debugInfo)}");
                        }

                        int remainingFlowersNeeded = totalFlowersNeeded;
                        foreach (var bft in batchFlowerTypes)
                        {
                            if (remainingFlowersNeeded <= 0) break;
                            int flowersToReduce = Math.Min(remainingFlowersNeeded, bft.CurrentQuantity);
                            bft.CurrentQuantity -= flowersToReduce;
                            remainingFlowersNeeded -= flowersToReduce;
                            _context.BatchFlowerTypes.Update(bft);
                        }

                        if (remainingFlowersNeeded > 0)
                        {
                            throw new Exception($"Không đủ hoa trong các lô còn hạn sử dụng cho {existingFlowerType.Name}.");
                        }

                        await _inventoryService.ExportInventoryAsync(
                            flowerType.FlowerTypeId,
                            totalFlowersNeeded,
                            $"Dùng để tạo {viewModel.Quantity} sản phẩm {viewModel.Name} ({flowerType.Quantity} bông/bó)",
                            User.Identity.Name ?? "Admin",
                            null
                        );

                        product.FlowerTypeProducts.Add(new FlowerTypeProduct
                        {
                            FlowerTypeId = flowerType.FlowerTypeId,
                            Quantity = flowerType.Quantity
                        });
                    }

                    await _productService.AddProductAsync(product);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Sản phẩm đã thêm thành công!";
                    return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
                }
                catch (Exception ex)
                {
                    var innerExceptionMessage = ex.InnerException?.Message ?? ex.Message;
                    ModelState.AddModelError("", $"Đã xảy ra lỗi: {innerExceptionMessage}");
                }
            }

            var categories = await _categoryService.GetAllCategoriesAsync();
            var flowerTypes = await _inventoryService.GetAllFlowerTypesAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "Name", viewModel.CategoryId);
            ViewBag.FlowerTypes = new SelectList(flowerTypes, "Id", "Name");
            ViewData["CurrentPage"] = currentPage;

            return View(viewModel);
        }

        private async Task<string> SaveImage(IFormFile image)
        {
            if (image == null || image.Length == 0)
            {
                return null;
            }

            var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
            var savePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images", fileName);

            using (var fileStream = new FileStream(savePath, FileMode.Create))
            {
                await image.CopyToAsync(fileStream);
            }

            return "/images/" + fileName;
        }

        [HttpGet]
        public async Task<IActionResult> Display(int id, int? currentPage)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound();
            }
            ViewData["CurrentPage"] = currentPage ?? 1;
            return View(product);
        }

        [HttpGet]
        public async Task<IActionResult> Update(int id, int? currentPage)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound();
            }

            var viewModel = new CreateProductViewModel
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Quantity = product.Quantity,
                Unit = product.Unit,
                IsActive = product.IsActive,
                IsNew = product.IsNew,
                CategoryId = product.CategoryId,
                ImageUrl = product.ImageUrl, // Ánh xạ ảnh chính
                AdditionalImageUrls = product.Images?.Select(img => img.Url).ToList() ?? new List<string>(), // Ánh xạ ảnh phụ
                FlowerTypes = product.FlowerTypeProducts?.Select(ftp => new FlowerTypeSelection
                {
                    FlowerTypeId = ftp.FlowerTypeId,
                    Quantity = ftp.Quantity
                }).ToList() ?? new List<FlowerTypeSelection>()
            };

            var categories = await _categoryService.GetAllCategoriesAsync();
            var flowerTypes = await _inventoryService.GetAllFlowerTypesAsync();

            ViewBag.Categories = new SelectList(categories, "Id", "Name", viewModel.CategoryId);
            ViewBag.FlowerTypes = new SelectList(flowerTypes, "Id", "Name");
            ViewData["CurrentPage"] = currentPage ?? 1;

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Update(int id, CreateProductViewModel viewModel, IFormFile imageUrl, List<IFormFile> additionalImages, int? currentPage)
        {
            ModelState.Remove("ImageUrl");
            if (id != viewModel.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingProduct = await _productService.GetProductByIdAsync(id);
                    if (existingProduct == null)
                    {
                        return NotFound();
                    }

                    if (viewModel.Price <= 0)
                    {
                        ModelState.AddModelError("Price", "Giá sản phẩm phải lớn hơn 0.");
                        throw new Exception("Giá sản phẩm không hợp lệ.");
                    }

                    if (viewModel.CategoryId <= 0)
                    {
                        ModelState.AddModelError("CategoryId", "Vui lòng chọn một danh mục hợp lệ.");
                        throw new Exception("Danh mục không hợp lệ.");
                    }

                    if (!viewModel.FlowerTypes.Any())
                    {
                        ModelState.AddModelError("FlowerTypes", "Vui lòng chọn ít nhất một loại hoa.");
                        throw new Exception("Chưa chọn loại hoa nào.");
                    }

                    existingProduct.Name = viewModel.Name;
                    existingProduct.Description = viewModel.Description;
                    existingProduct.Price = viewModel.Price;
                    existingProduct.Quantity = viewModel.Quantity;
                    existingProduct.Unit = viewModel.Unit;
                    existingProduct.IsActive = viewModel.IsActive;
                    existingProduct.IsNew = viewModel.IsNew;
                    existingProduct.CategoryId = viewModel.CategoryId;

                    if (imageUrl != null && imageUrl.Length > 0)
                    {
                        var imagePath = await SaveImage(imageUrl);
                        existingProduct.ImageUrl = imagePath;
                    }

                    if (additionalImages != null && additionalImages.Any())
                    {
                        existingProduct.Images.Clear();
                        foreach (var image in additionalImages)
                        {
                            if (image != null && image.Length > 0)
                            {
                                var imgPath = await SaveImage(image);
                                existingProduct.Images.Add(new ProductImage { Url = imgPath });
                            }
                        }
                    }

                    var existingFlowerTypeProducts = await _context.FlowerTypeProducts
                        .Where(ftp => ftp.ProductId == id)
                        .ToListAsync();
                    var oldQuantities = existingFlowerTypeProducts.ToDictionary(ftp => ftp.FlowerTypeId, ftp => ftp.Quantity * existingProduct.Quantity);

                    _context.FlowerTypeProducts.RemoveRange(existingFlowerTypeProducts);

                    var newFlowerTypeProducts = new List<FlowerTypeProduct>();
                    foreach (var flowerType in viewModel.FlowerTypes)
                    {
                        var existingFlowerType = await _context.FlowerTypes
                            .Include(ft => ft.BatchFlowerTypes)
                            .ThenInclude(bft => bft.Batch)
                            .FirstOrDefaultAsync(ft => ft.Id == flowerType.FlowerTypeId);
                        if (existingFlowerType == null)
                        {
                            throw new Exception($"Loại hoa với ID {flowerType.FlowerTypeId} không tồn tại.");
                        }

                        int totalFlowersNeeded = flowerType.Quantity * viewModel.Quantity;
                        int oldTotalFlowers = oldQuantities.ContainsKey(flowerType.FlowerTypeId) ? oldQuantities[flowerType.FlowerTypeId] : 0;
                        int flowerDifference = totalFlowersNeeded - oldTotalFlowers;

                        if (flowerDifference > 0)
                        {
                            if (existingFlowerType.Quantity < flowerDifference)
                            {
                                throw new Exception($"Số lượng tồn kho của {existingFlowerType.Name} không đủ. Cần {flowerDifference} bông, nhưng chỉ có {existingFlowerType.Quantity} trong kho.");
                            }

                            var batchFlowerTypes = existingFlowerType.BatchFlowerTypes
                                .Where(bft => bft.CurrentQuantity > 0 && bft.Batch != null && bft.Batch.ExpiryDate > DateTime.Now)
                                .OrderBy(bft => bft.Batch.ExpiryDate)
                                .ToList();
                            int remainingFlowersNeeded = flowerDifference;
                            foreach (var bft in batchFlowerTypes)
                            {
                                if (remainingFlowersNeeded <= 0) break;
                                int flowersToReduce = Math.Min(remainingFlowersNeeded, bft.CurrentQuantity);
                                bft.CurrentQuantity -= flowersToReduce;
                                remainingFlowersNeeded -= flowersToReduce;
                                _context.BatchFlowerTypes.Update(bft);
                            }

                            if (remainingFlowersNeeded > 0)
                            {
                                throw new Exception($"Không đủ hoa trong các lô còn hạn sử dụng cho {existingFlowerType.Name}.");
                            }

                            await _inventoryService.ExportInventoryAsync(
                                flowerType.FlowerTypeId,
                                flowerDifference,
                                $"Cập nhật sản phẩm {viewModel.Name} ({flowerType.Quantity} bông/bó)",
                                User.Identity.Name ?? "Admin",
                                null
                            );
                        }
                        else if (flowerDifference < 0)
                        {
                            int flowersToAdd = -flowerDifference; // Số hoa cần cộng lại
                            var batchFlowerTypes = existingFlowerType.BatchFlowerTypes
                                .Where(bft => bft.Batch != null && bft.Batch.ExpiryDate > DateTime.Now)
                                .OrderBy(bft => bft.Batch.ExpiryDate)
                                .ToList();
                            int remainingFlowersToAdd = flowersToAdd;
                            foreach (var bft in batchFlowerTypes)
                            {
                                if (remainingFlowersToAdd <= 0) break;
                                int flowersToIncrease = Math.Min(remainingFlowersToAdd, flowersToAdd); // Tăng tối đa số hoa cần
                                bft.CurrentQuantity += flowersToIncrease;
                                remainingFlowersToAdd -= flowersToIncrease;
                                _context.BatchFlowerTypes.Update(bft);
                            }

                            if (remainingFlowersToAdd > 0)
                            {
                                throw new Exception($"Không thể cộng lại đủ hoa vào các lô còn hạn sử dụng cho {existingFlowerType.Name}.");
                            }

                            await _inventoryService.ImportFlowerTypeAsync(
                                flowerType.FlowerTypeId,
                                flowersToAdd,
                                $"Hoàn lại từ cập nhật sản phẩm {viewModel.Name} ({flowerType.Quantity} bông/bó)",
                                User.Identity.Name ?? "Admin",
                                null,
                                null
                            );
                        }

                        newFlowerTypeProducts.Add(new FlowerTypeProduct
                        {
                            ProductId = id,
                            FlowerTypeId = flowerType.FlowerTypeId,
                            Quantity = flowerType.Quantity
                        });
                    }

                    var duplicateFlowerTypes = newFlowerTypeProducts
                        .GroupBy(ftp => ftp.FlowerTypeId)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key)
                        .ToList();
                    if (duplicateFlowerTypes.Any())
                    {
                        throw new Exception($"Danh sách loại hoa chứa loại hoa trùng lặp: {string.Join(", ", duplicateFlowerTypes)}.");
                    }

                    existingProduct.FlowerTypeProducts = newFlowerTypeProducts;

                    await _productService.UpdateProductAsync(existingProduct);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Sản phẩm đã được cập nhật thành công!";
                    return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
                }
                catch (Exception ex)
                {
                    var innerExceptionMessage = ex.InnerException?.Message ?? ex.Message;
                    ModelState.AddModelError("", $"Đã xảy ra lỗi: {innerExceptionMessage}");
                }
            }

            var categories = await _categoryService.GetAllCategoriesAsync();
            var flowerTypes = await _inventoryService.GetAllFlowerTypesAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "Name", viewModel.CategoryId);
            ViewBag.FlowerTypes = new SelectList(flowerTypes, "Id", "Name");
            ViewData["CurrentPage"] = currentPage;

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int id, int? currentPage)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound();
            }
            ViewData["CurrentPage"] = currentPage ?? 1;
            return View(product);
        }

        [HttpPost, ActionName("DeleteConfirmed")]
        public async Task<IActionResult> DeleteConfirmed(int id, int currentPage)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound();
            }

            // Lấy danh sách FlowerTypeProducts của sản phẩm
            var flowerTypeProducts = await _context.FlowerTypeProducts
                .Where(ftp => ftp.ProductId == id)
                .Include(ftp => ftp.FlowerType)
                .ThenInclude(ft => ft.BatchFlowerTypes)
                .ThenInclude(bft => bft.Batch)
                .ToListAsync();

            // Cộng lại số lượng hoa vào kho
            foreach (var ftp in flowerTypeProducts)
            {
                int totalFlowersUsed = ftp.Quantity * product.Quantity;
                var existingFlowerType = ftp.FlowerType;
                if (existingFlowerType != null)
                {
                    var batchFlowerTypes = existingFlowerType.BatchFlowerTypes
                        .Where(bft => bft.Batch != null && bft.Batch.ExpiryDate > DateTime.Now)
                        .OrderBy(bft => bft.Batch.ExpiryDate)
                        .ToList();
                    int remainingFlowersToAdd = totalFlowersUsed;
                    foreach (var bft in batchFlowerTypes)
                    {
                        if (remainingFlowersToAdd <= 0) break;
                        int flowersToIncrease = Math.Min(remainingFlowersToAdd, totalFlowersUsed); // Tăng tối đa số hoa cần
                        bft.CurrentQuantity += flowersToIncrease;
                        remainingFlowersToAdd -= flowersToIncrease;
                        _context.BatchFlowerTypes.Update(bft);
                    }

                    if (remainingFlowersToAdd > 0)
                    {
                        // Log lỗi nếu không thể cộng đủ (có thể bỏ qua nếu không ảnh hưởng lớn)
                        Console.WriteLine($"Không thể cộng lại đủ {remainingFlowersToAdd} hoa cho {existingFlowerType.Name} khi xóa sản phẩm.");
                    }

                    await _inventoryService.ImportFlowerTypeAsync(
                        ftp.FlowerTypeId,
                        totalFlowersUsed,
                        $"Hoàn lại từ xóa sản phẩm {product.Name}",
                        User.Identity.Name ?? "Admin",
                        null,
                        null
                    );
                }
            }

            await _productService.DeleteProductAsync(id);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Sản phẩm đã được xóa thành công!";
            return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
        }

        [HttpGet]
        public async Task<IActionResult> GetProductsByCategory(int categoryId)
        {
            var products = await _productService.GetAllProductsAsync();
            var filteredProducts = products.Where(p => p.CategoryId == categoryId).ToList();
            return PartialView("_ProductsByCategoryPartial", filteredProducts);
        }
    }
}
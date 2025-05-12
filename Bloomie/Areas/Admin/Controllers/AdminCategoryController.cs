using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc;
using Bloomie.Areas.Admin.Models;
using Bloomie.Services.Interfaces;
using Bloomie.Models.ViewModels;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = Role.Role_Admin)]
    public class AdminCategoryController : Controller
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;

        public AdminCategoryController(IProductService productService, ICategoryService categoryService)
        {
            _productService = productService;
            _categoryService = categoryService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var categories = await _categoryService.GetAllCategoriesAsync();
            return View(categories);
        }

        [HttpGet]
        public async Task<IActionResult> Add()
        {
            var topLevelCategories = await _categoryService.GetTopLevelCategoriesAsync();
            ViewBag.ParentCategories = new SelectList(topLevelCategories, "Id", "Name");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Add(Category category)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _categoryService.AddCategoryAsync(category);
                    TempData["SuccessMessage"] = "Thêm danh mục thành công!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Lỗi khi thêm danh mục: {ex.Message}");
                }
            }

            var topLevelCategories = await _categoryService.GetTopLevelCategoriesAsync();
            ViewBag.ParentCategories = new SelectList(topLevelCategories, "Id", "Name", category.ParentCategoryId);
            return View(category);
        }

        [HttpGet]
        public async Task<IActionResult> Display(int id)
        {
            var category = await _categoryService.GetCategoryByIdAsync(id);
            if (category == null)
            {
                return NotFound();
            }
            return View(category);
        }

        [HttpGet]
        public async Task<IActionResult> Update(int id)
        {
            var category = await _categoryService.GetCategoryByIdAsync(id);
            if (category == null)
            {
                return NotFound();
            }

            var topLevelCategories = await _categoryService.GetTopLevelCategoriesAsync();
            ViewBag.ParentCategories = new SelectList(topLevelCategories, "Id", "Name", category.ParentCategoryId);

            var products = await _productService.GetAllProductsAsync();
            var categoryProducts = products.Where(p => p.CategoryId == id).ToList();
            ViewBag.CategoryProducts = categoryProducts;

            return View(category);
        }

        [HttpPost]
        public async Task<IActionResult> Update(int id, Category category)
        {
            if (id != category.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    await _categoryService.UpdateCategoryAsync(category);
                    TempData["SuccessMessage"] = "Cập nhật danh mục thành công!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Lỗi khi cập nhật danh mục: {ex.Message}");
                }
            }

            var topLevelCategories = await _categoryService.GetTopLevelCategoriesAsync();
            ViewBag.ParentCategories = new SelectList(topLevelCategories, "Id", "Name", category.ParentCategoryId);
            var products = await _productService.GetAllProductsAsync();
            var categoryProducts = products.Where(p => p.CategoryId == id).ToList();
            ViewBag.CategoryProducts = categoryProducts;

            return View(category);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _categoryService.GetCategoryByIdAsync(id);
            if (category == null)
            {
                return NotFound();
            }
            return View(category);
        }

        [HttpPost, ActionName("DeleteConfirmed")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                await _categoryService.DeleteCategoryAsync(id);
                TempData["SuccessMessage"] = "Xóa danh mục thành công!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xóa danh mục: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> AssignSubCategories()
        {
            var model = new AssignSubCategoriesViewModel
            {
                ParentCategories = (await _categoryService.GetTopLevelCategoriesAsync()).ToList(),
                SubCategories = (await _categoryService.GetAllCategoriesAsync())
                    .Where(c => c.ParentCategoryId == null)
                    .ToList()
            };
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AssignSubCategories(AssignSubCategoriesViewModel model)
        {
            if (model.ParentCategoryId == 0 || model.SubCategoryIds == null || !model.SubCategoryIds.Any())
            {
                ModelState.AddModelError("", "Vui lòng chọn danh mục cha và ít nhất một danh mục con.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var parentCategory = await _categoryService.GetCategoryByIdAsync(model.ParentCategoryId);
                    if (parentCategory == null)
                    {
                        return NotFound();
                    }

                    foreach (var subCategoryId in model.SubCategoryIds)
                    {
                        var subCategory = await _categoryService.GetCategoryByIdAsync(subCategoryId);
                        if (subCategory != null)
                        {
                            subCategory.ParentCategoryId = model.ParentCategoryId;
                            await _categoryService.UpdateCategoryAsync(subCategory);
                        }
                    }

                    TempData["SuccessMessage"] = "Gán danh mục con thành công!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Lỗi khi gán danh mục con: {ex.Message}");
                }
            }

            // Khi validation thất bại, tải lại dữ liệu
            model.ParentCategories = (await _categoryService.GetTopLevelCategoriesAsync()).ToList();
            model.SubCategories = (await _categoryService.GetAllCategoriesAsync())
                .Where(c => c.ParentCategoryId == null && c.Id != model.ParentCategoryId)
                .ToList();

            return View(model);
        }
    }
}

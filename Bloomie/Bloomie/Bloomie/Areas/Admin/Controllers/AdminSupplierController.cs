using Bloomie.Data;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminSupplierController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminSupplierController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Admin/AdminSupplier/Index
        [HttpGet]
        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10)
        {
            var suppliers = await _context.Suppliers
                .ToListAsync(); // Bỏ điều kiện Where(s => s.IsActive) để hiển thị tất cả

            int totalItems = suppliers.Count;
            var pagedSuppliers = suppliers
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewData["PageSize"] = pageSize;

            return View(pagedSuppliers);
        }

        // GET: /Admin/AdminSupplier/Add
        [HttpGet]
        public IActionResult Add()
        {
            return View();
        }

        // POST: /Admin/AdminSupplier/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(Supplier supplier)
        {
            if (ModelState.IsValid)
            {
                supplier.IsActive = true; // Đặt mặc định là true (Hoạt động)
                _context.Add(supplier);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Đã thêm nhà cung cấp {supplier.Name}.";
                return RedirectToAction(nameof(Index));
            }
            return View(supplier);
        }

        // GET: /Admin/AdminSupplier/Update/5
        [HttpGet]
        public async Task<IActionResult> Update(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) return NotFound();
            return View(supplier);
        }

        // POST: /Admin/AdminSupplier/Update/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int id, Supplier supplier)
        {
            if (id != supplier.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(supplier);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Đã cập nhật nhà cung cấp {supplier.Name}.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SupplierExists(supplier.Id))
                    {
                        return NotFound();
                    }
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(supplier);
        }

        // GET: /Admin/AdminSupplier/Delete/5
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) return NotFound();
            return View(supplier);
        }

        // POST: /Admin/AdminSupplier/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier != null)
            {
                supplier.IsActive = false; // Soft delete
                _context.Update(supplier);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Đã xóa nhà cung cấp {supplier.Name}.";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool SupplierExists(int id)
        {
            return _context.Suppliers.Any(e => e.Id == id);
        }
    }
}
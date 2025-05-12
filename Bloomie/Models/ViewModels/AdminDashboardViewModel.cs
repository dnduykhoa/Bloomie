using System;
using System.Collections.Generic;
using Bloomie.Models.Entities;

namespace Bloomie.Models.ViewModels
{
    public class AdminDashboardViewModel
    {
        public int TotalProducts { get; set; }
        public int TotalOrders { get; set; }
        public int TotalSuppliers { get; set; }
        public int TotalUsers { get; set; }
        public int TotalCategories { get; set; }
        public int TotalPromotions { get; set; }
        public int TotalAccessCount { get; set; }
        public int LowStockProductCount { get; set; }
        public int OutOfStockProductCount { get; set; }
        public List<Product> LowStockProducts { get; set; }
        public List<RevenueSummary> RevenueSummaries { get; set; }
        // Tổng doanh thu thời gian thực
        public decimal DailyRevenue { get; set; }
        public decimal WeeklyRevenue { get; set; }
        // Thông báo nhanh
        public List<string> Notifications { get; set; }
        // Doanh thu theo danh mục
        public List<CategoryRevenueSummary> CategoryRevenueSummaries { get; set; }
        // Lọc theo danh mục
        public List<Category> Categories { get; set; }
    }

    public class RevenueSummary
    {
        public int Month { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class CategoryRevenueSummary
    {
        public string CategoryName { get; set; }
        public decimal TotalRevenue { get; set; }
    }
}
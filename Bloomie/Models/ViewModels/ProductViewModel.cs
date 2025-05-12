using Bloomie.Models.Entities;

namespace Bloomie.Models.ViewModels
{
    public class ProductViewModel
    {
        public List<Product> Products { get; set; } // Danh sách sản phẩm chính
        public List<Product> NewProducts { get; set; } // Danh sách sản phẩm mới
        public List<Product> BestSellingProducts { get; set; } // Danh sách sản phẩm bán chạy
        public List<Product> BestSellingWithPromotions { get; set; } // Sản phẩm bán chạy có khuyến mãi
        public List<Product> NewWithPromotions { get; set; } // Sản phẩm mới có khuyến mãi
    }
}

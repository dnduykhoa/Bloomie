namespace Bloomie.Models.Entities
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? ParentCategoryId { get; set; }
        public Category? ParentCategory { get; set; }
        public List<Category>? SubCategories { get; set; } = new List<Category>();
        public List<Product>? Products { get; set; }
    }
}

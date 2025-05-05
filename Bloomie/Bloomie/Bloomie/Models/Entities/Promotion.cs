namespace Bloomie.Models.Entities
{
    public class Promotion
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public decimal DiscountPercentage { get; set; }
        public decimal? MinimumOrderValue { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; }
        public List<PromotionProduct> PromotionProducts { get; set; } = new List<PromotionProduct>();
    }
}

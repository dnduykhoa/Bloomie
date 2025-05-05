using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class Review
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }
        public int ProductId { get; set; }
        public Product Product { get; set; }
        public int Rating { get; set; } // 1 - 5 sao
        public string Comment { get; set; }
        public DateTime ReviewDate { get; set; }
    }
}

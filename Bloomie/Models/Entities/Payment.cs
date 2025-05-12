namespace Bloomie.Models.Entities
{
    public class Payment
    {
        public int Id { get; set; }
        public string OrderId { get; set; }
        public Order Order { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } // Momo, ZaloPay, ATM
        public string PaymentStatus { get; set; } // Pending, Paid, Failed
        public DateTime PaymentDate { get; set; }
    }
}

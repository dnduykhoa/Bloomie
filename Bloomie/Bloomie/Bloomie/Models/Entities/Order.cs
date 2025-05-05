using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public enum OrderStatus
    {
        Pending,
        Processing,
        Shipped,
        Delivered,
        Cancelled
    }

    public class Order
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal TotalPrice { get; set; }
        public string ShippingAddress { get; set; }
        public string PhoneNumber { get; set; }
        public string? Notes { get; set; }
        public OrderStatus OrderStatus { get; set; } = OrderStatus.Pending;
        public List<OrderDetail> OrderDetails { get; set; } = new();
        public Payment? Payment { get; set; }
        public int? PromotionId { get; set; }
        public Promotion? Promotion { get; set; }
        public DateTime? DeliveryDate { get; set; } // Ngày giao hàng dự kiến
        public string ShippingMethod { get; set; }
        public List<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
        public List<InventoryTransaction> Transactions { get; set; } = new List<InventoryTransaction>();
    }
}

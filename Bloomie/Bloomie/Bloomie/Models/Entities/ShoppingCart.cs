namespace Bloomie.Models.Entities
{
    public class ShoppingCart
    {
        public string UserId { get; set; }
        public List<CartItem> Items { get; set; } = new List<CartItem>();
        public decimal TotalPrice => Items.Sum(item => item.DiscountedPrice * item.Quantity);
        public int TotalItems
        {
            get { return Items.Sum(item => item.Quantity); }
        }
        public void AddItem(CartItem item)
        {
            var existingItem = Items.FirstOrDefault(i => i.ProductId == item.ProductId);
            if (existingItem != null)
            {
                existingItem.Quantity += item.Quantity;
            }
            else
            {
                Items.Add(item);
            }
        }
        public void RemoveItem(int productId)
        {
            Items.RemoveAll(i => i.ProductId == productId);
        }
    }
}

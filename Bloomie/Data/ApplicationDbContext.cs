using System.Reflection.Emit;
using Bloomie.Models;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Product> Products { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<Promotion> Promotions { get; set; }
        public DbSet<PromotionProduct> PromotionProducts { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }
        public DbSet<InventoryTransaction> InventoryTransactions { get; set; }
        public DbSet<FlowerType> FlowerTypes { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<Batch> Batches { get; set; }
        public DbSet<BatchFlowerType> BatchFlowerTypes { get; set; }
        public DbSet<FlowerTypeProduct> FlowerTypeProducts { get; set; }
        public DbSet<UserAccessLog> UserAccessLogs { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Shipping> Shippings { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Cấu hình mối quan hệ giữa Product và Category (1-n)
            builder.Entity<Product>()
                .HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId);


            // Cấu hình mối quan hệ giữa Category và ParentCategory (cây phân cấp)
            builder.Entity<Category>()
                .HasOne(c => c.ParentCategory)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(c => c.ParentCategoryId);


            // Cấu hình mối quan hệ giữa Product và ProductImage (1-n)
            builder.Entity<ProductImage>()
                .HasOne(pi => pi.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(pi => pi.ProductId);


            // Cấu hình mối quan hệ nhiều-nhiều giữa Product và Promotion thông qua PromotionProduct
            builder.Entity<PromotionProduct>()
                .HasKey(pp => new { pp.PromotionId, pp.ProductId }); // Khóa chính composite


            builder.Entity<PromotionProduct>()
                .HasOne(pp => pp.Product)
                .WithMany(p => p.PromotionProducts)
                .HasForeignKey(pp => pp.ProductId);


            builder.Entity<PromotionProduct>()
                .HasOne(pp => pp.Promotion)
                .WithMany(p => p.PromotionProducts)
                .HasForeignKey(pp => pp.PromotionId);

            builder.Entity<Review>()
                .HasOne(r => r.Product)
                .WithMany(p => p.Reviews)
                .HasForeignKey(r => r.ProductId);

            builder.Entity<Review>()
                .HasOne(r => r.User)
                .WithMany(u => u.Reviews)
                .HasForeignKey(r => r.UserId);

            // Quan hệ nhiều-nhiều giữa FlowerType và Supplier
            builder.Entity<FlowerType>()
                .HasMany(ft => ft.Suppliers)
                .WithMany(s => s.FlowerTypes)
                .UsingEntity(j => j.ToTable("FlowerTypeSuppliers"));

            // Cấu hình mối quan hệ nhiều-nhiều giữa FlowerType và Product thông qua FlowerTypeProduct
            builder.Entity<FlowerTypeProduct>()
                .HasKey(ftp => new { ftp.FlowerTypeId, ftp.ProductId });

            builder.Entity<FlowerTypeProduct>()
                .HasOne(ftp => ftp.FlowerType)
                .WithMany(ft => ft.FlowerTypeProducts)
                .HasForeignKey(ftp => ftp.FlowerTypeId);

            builder.Entity<FlowerTypeProduct>()
                .HasOne(ftp => ftp.Product)
                .WithMany(p => p.FlowerTypeProducts)
                .HasForeignKey(ftp => ftp.ProductId);

            // Cấu hình mối quan hệ nhiều-nhiều giữa Batch và FlowerType thông qua BatchFlowerType
            builder.Entity<BatchFlowerType>()
                .HasKey(bft => new { bft.BatchId, bft.FlowerTypeId });

            builder.Entity<BatchFlowerType>()
                .HasOne(bft => bft.Batch)
                .WithMany(b => b.BatchFlowerTypes)
                .HasForeignKey(bft => bft.BatchId);

            builder.Entity<BatchFlowerType>()
                .HasOne(bft => bft.FlowerType)
                .WithMany(ft => ft.BatchFlowerTypes)
                .HasForeignKey(bft => bft.FlowerTypeId);

            // Cấu hình mối quan hệ 1-N giữa FlowerType và InventoryTransaction
            builder.Entity<InventoryTransaction>()
                .HasOne(it => it.FlowerType)
                .WithMany(ft => ft.Transactions)
                .HasForeignKey(it => it.FlowerTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cấu hình mối quan hệ 1-N giữa Supplier và Batch
            builder.Entity<Batch>()
                .HasOne(b => b.Supplier)
                .WithMany(s => s.Batches)
                .HasForeignKey(b => b.SupplierId);

            // Cấu hình mối quan hệ 1-N giữa Supplier và InventoryTransaction
            builder.Entity<InventoryTransaction>()
                .HasOne(it => it.Supplier)
                .WithMany(s => s.Transactions)
                .HasForeignKey(it => it.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cấu hình mối quan hệ 1-N giữa Batch và InventoryTransaction
            builder.Entity<InventoryTransaction>()
                .HasOne(it => it.Batch)
                .WithMany(b => b.Transactions)
                .HasForeignKey(it => it.BatchId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OrderTracker.Domain.Orders;

namespace OrderTracker.Infrastructure.Persistence;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    /// <summary>The unique index that makes duplicate orders impossible; the unit of work recognises it by name.</summary>
    public const string UniqueReferenceIndex = "ux_orders_normalized_reference";

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
}

/// <summary>
/// Maps the <see cref="Order"/> aggregate directly: value objects become owned columns or converted values,
/// child entities become owned tables. The domain stays free of EF attributes. Table and column names match
/// the original schema exactly.
/// </summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    private static readonly ValueConverter<CurrencyCode, string> CurrencyConverter = new(c => c.Value, v => CurrencyCode.From(v));
    private static readonly ValueConverter<OrderId, Guid> OrderIdConverter = new(id => id.Value, v => new OrderId(v));

    public void Configure(EntityTypeBuilder<Order> order)
    {
        var statuses = string.Join(", ", Enum.GetNames<OrderStatus>().Select(s => $"'{s}'"));

        order.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_orders_status", $"status IN ({statuses})");
            t.HasCheckConstraint("ck_orders_currency", "currency ~ '^[A-Z]{3}$' AND base_currency ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_orders_totals", "subtotal >= 0 AND total >= 0");
            t.HasCheckConstraint("ck_orders_version", "version >= 1");
        });

        order.HasKey(o => o.Id);
        order.Property(o => o.Id).HasConversion(OrderIdConverter).ValueGeneratedNever();
        order.Property(o => o.Number).UseIdentityAlwaysColumn();
        order.HasIndex(o => o.Number).IsUnique();

        order.OwnsOne(o => o.Reference, r =>
        {
            r.Property(x => x.Value).HasColumnName("external_reference").HasMaxLength(ExternalReference.MaxLength);
            r.Property(x => x.Normalized).HasColumnName("normalized_reference").HasMaxLength(ExternalReference.MaxLength);
            r.HasIndex(x => x.Normalized).IsUnique().HasDatabaseName(OrdersDbContext.UniqueReferenceIndex);
        });
        order.Navigation(o => o.Reference).IsRequired();

        order.OwnsOne(o => o.Customer, c =>
        {
            c.Property(x => x.Id).HasColumnName("customer_id").HasMaxLength(32).IsRequired();
            c.Property(x => x.Email).HasColumnName("customer_email").HasMaxLength(EmailAddress.MaxLength);
            c.Property(x => x.Name).HasColumnName("customer_name").HasMaxLength(CustomerInfo.MaxNameLength);
        });
        order.Navigation(o => o.Customer).IsRequired();

        order.Property(o => o.Currency).HasConversion(CurrencyConverter).HasMaxLength(3).IsFixedLength();
        order.Property(o => o.BaseCurrency).HasConversion(CurrencyConverter).HasMaxLength(3).IsFixedLength();
        order.Property(o => o.Notes).HasMaxLength(Order.MaxNotesLength);
        order.Property(o => o.Subtotal).HasPrecision(28, 2);
        order.Property(o => o.Total).HasPrecision(28, 2);
        order.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        order.Property(o => o.Version).IsConcurrencyToken();
        order.Property(o => o.SubmissionFingerprint).HasMaxLength(64).IsFixedLength();

        // Supports the "newest first" listing and the status filter.
        order.HasIndex(o => new { o.CreatedAt, o.Number }).IsDescending(true, true).HasDatabaseName("ix_orders_created_at_number");
        order.HasIndex(o => o.Status);

        order.Ignore(o => o.DomainEvents);
        order.Ignore(o => o.AllowedTransitions);
        order.Ignore(o => o.CanChangeCurrency);

        order.OwnsMany(o => o.Lines, line =>
        {
            line.ToTable("order_lines", t =>
            {
                t.HasCheckConstraint("ck_order_lines_quantity", "quantity > 0");
                t.HasCheckConstraint("ck_order_lines_prices", "unit_price >= 0 AND base_unit_price >= 0 AND line_total >= 0");
            });
            line.WithOwner().HasForeignKey("OrderId");
            line.Property<OrderId>("OrderId").HasConversion(OrderIdConverter);
            line.HasKey("OrderId", nameof(OrderLine.LineNo));
            line.Property(l => l.LineNo).ValueGeneratedNever();
            line.Property(l => l.Sku).HasMaxLength(OrderLine.MaxSkuLength);
            line.Property(l => l.Name).HasMaxLength(OrderLine.MaxNameLength);
            line.Property(l => l.UnitPrice).HasPrecision(20, 2);
            line.Property(l => l.BaseUnitPrice).HasPrecision(20, 2);
            line.Property(l => l.LineTotal).HasPrecision(28, 2);
        });
        order.Navigation(o => o.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        order.OwnsMany(o => o.StatusHistory, change =>
        {
            change.ToTable("order_status_changes");
            change.WithOwner().HasForeignKey("OrderId");
            change.Property<OrderId>("OrderId").HasConversion(OrderIdConverter);
            change.HasKey(c => c.Id);
            change.Property(c => c.Id).UseIdentityAlwaysColumn();
            change.Property(c => c.From).HasColumnName("from_status").HasConversion<string>().HasMaxLength(16);
            change.Property(c => c.To).HasColumnName("to_status").HasConversion<string>().HasMaxLength(16);
            change.Property(c => c.At).HasColumnName("changed_at");
            change.HasIndex("OrderId", nameof(OrderStatusChange.Id));
        });
        order.Navigation(o => o.StatusHistory).HasField("_statusHistory").UsePropertyAccessMode(PropertyAccessMode.Field);

        order.OwnsMany(o => o.CurrencyHistory, change =>
        {
            change.ToTable("order_currency_changes", t => t.HasCheckConstraint("ck_order_currency_changes_rate", "rate_from_base > 0"));
            change.WithOwner().HasForeignKey("OrderId");
            change.Property<OrderId>("OrderId").HasConversion(OrderIdConverter);
            change.HasKey(c => c.Id);
            change.Property(c => c.Id).UseIdentityAlwaysColumn();
            change.Property(c => c.From).HasColumnName("from_currency").HasConversion(CurrencyConverter).HasMaxLength(3).IsFixedLength();
            change.Property(c => c.To).HasColumnName("to_currency").HasConversion(CurrencyConverter).HasMaxLength(3).IsFixedLength();
            change.Property(c => c.RateFromBase).HasPrecision(20, 8);
            change.Property(c => c.RateSource).HasMaxLength(100);
            change.Property(c => c.At).HasColumnName("changed_at");
            change.HasIndex("OrderId", nameof(OrderCurrencyChange.Id));
        });
        order.Navigation(o => o.CurrencyHistory).HasField("_currencyHistory").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

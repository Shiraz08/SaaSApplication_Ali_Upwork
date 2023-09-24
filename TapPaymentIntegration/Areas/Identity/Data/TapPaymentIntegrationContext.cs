using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TapPaymentIntegration.Areas.Identity.Data;
using TapPaymentIntegration.Models.Card;
using TapPaymentIntegration.Models.InvoiceDTO;
using TapPaymentIntegration.Models.PaymentDTO;

namespace TapPaymentIntegration.Data;

public class TapPaymentIntegrationContext : IdentityDbContext<ApplicationUser>
{
    public TapPaymentIntegrationContext(DbContextOptions<TapPaymentIntegrationContext> options)
        : base(options)
    {
    }
    public DbSet<Subscriptions> subscriptions { get; set; }
    public DbSet<ChargeResponse>  chargeResponses { get; set; }
    public DbSet<Invoice>  invoices { get; set; }
    public DbSet<RecurringCharge>  recurringCharges { get; set; } 
    public DbSet<ChangeCardInfo> changeCardInfos { get; set; } 
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(b =>
        {
            b.Property(p => p.Id)
                .HasMaxLength(36);
        });

    }
}

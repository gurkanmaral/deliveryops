using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class BusinessCreditAccountConfiguration : IEntityTypeConfiguration<BusinessCreditAccount>
{
    public void Configure(EntityTypeBuilder<BusinessCreditAccount> builder)
    {
        builder.ToTable("business_credit_accounts");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.BusinessId).IsUnique();
        builder.HasOne<Business>().WithOne().HasForeignKey<BusinessCreditAccount>(x => x.BusinessId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.Version).IsRowVersion();
    }
}

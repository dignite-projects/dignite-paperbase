using Dignite.Paperbase.Contracts.Contracts;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Dignite.Paperbase.Contracts.EntityFrameworkCore;

public static class ContractsDbContextModelCreatingExtensions
{
    public static void ConfigureContracts(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Contract>(b =>
        {
            b.ToTable(ContractsDbProperties.DbTablePrefix + "Contracts", ContractsDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.HasIndex(x => x.DocumentId).IsUnique();
            b.HasIndex(x => x.ExpirationDate);
            b.HasIndex(x => x.CounterpartyName);
            b.HasIndex(x => x.Status);

            b.Property(x => x.DocumentTypeCode).HasMaxLength(128).IsRequired();
            b.Property(x => x.Title).HasMaxLength(256);
            b.Property(x => x.ContractNumber).HasMaxLength(64);
            b.Property(x => x.PartyAName).HasMaxLength(256);
            b.Property(x => x.PartyBName).HasMaxLength(256);
            b.Property(x => x.CounterpartyName).HasMaxLength(256);
            b.Property(x => x.Currency).HasMaxLength(8);
            b.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
        });
    }
}

using Dignite.Paperbase.Contracts;
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

            b.Property(x => x.DocumentTypeCode).HasMaxLength(ContractConsts.MaxDocumentTypeCodeLength).IsRequired();
            b.Property(x => x.Title).HasMaxLength(ContractConsts.MaxTitleLength);
            b.Property(x => x.ContractNumber).HasMaxLength(ContractConsts.MaxContractNumberLength);
            b.Property(x => x.PartyAName).HasMaxLength(ContractConsts.MaxPartyNameLength);
            b.Property(x => x.PartyBName).HasMaxLength(ContractConsts.MaxPartyNameLength);
            b.Property(x => x.CounterpartyName).HasMaxLength(ContractConsts.MaxPartyNameLength);
            b.Property(x => x.Currency).HasMaxLength(ContractConsts.MaxCurrencyLength);
            b.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
            b.Property(x => x.GoverningLaw).HasMaxLength(ContractConsts.MaxGoverningLawLength);
            b.Property(x => x.Summary).HasMaxLength(ContractConsts.MaxSummaryLength);
        });
    }
}

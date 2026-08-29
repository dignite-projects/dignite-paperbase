using System;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.EntityFrameworkCore;
using Dignite.Vault.Extract.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace Dignite.Vault.Extract.Documents;

/// <summary>
/// Keeps <see cref="DocumentFlexFieldIndex"/> in step with the documents' value bags (#558).
/// <para>
/// Paging, deciding which values are eligible, fanning a multi-valued field out into one row per value,
/// and typing each one into its slot all come from the kernel. The two overrides are the only things it
/// cannot know: the name of Vault Extract's own foreign key, and how to build one of its rows.
/// </para>
/// <para>
/// Resolves as <c>IFlexFieldIndexManager&lt;Document&gt;</c> by ABP's naming convention.
/// </para>
/// </summary>
public class DocumentFlexFieldIndexManager
    : EfCoreFlexFieldIndexManagerBase<VaultExtractDbContext, Document, DocumentFlexFieldIndex>,
      ITransientDependency
{
    protected ICurrentTenant CurrentTenant { get; }

    protected IGuidGenerator GuidGenerator { get; }

    protected override string EntityIdPropertyName => nameof(DocumentFlexFieldIndex.DocumentId);

    public DocumentFlexFieldIndexManager(
        IDbContextProvider<VaultExtractDbContext> dbContextProvider,
        IFlexFieldProvider<Document> flexFieldProvider,
        IFieldTypeResolver fieldTypeResolver,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator)
        : base(dbContextProvider, flexFieldProvider, fieldTypeResolver)
    {
        CurrentTenant = currentTenant;
        GuidGenerator = guidGenerator;
    }

    protected override DocumentFlexFieldIndex CreateIndexRow(Guid entityId, Guid fieldId, FlexFieldIndexValue value)
    {
        return new DocumentFlexFieldIndex(GuidGenerator.Create(), entityId, fieldId, value, CurrentTenant.Id);
    }
}

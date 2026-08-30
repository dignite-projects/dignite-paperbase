using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents.Fields;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Dignite.Vault.Extract.Documents;

/// <summary>
/// The one seam Vault Extract implements for the FlexFields kernel (#558): given a document, which fields
/// does it have and what are their values.
/// <para>
/// The kernel owns no field or host model, so everything it does downstream — validation, index
/// maintenance, query pushdown, rename migration — is driven from what this returns. Three sources are
/// merged: the <see cref="Field"/> definition (what the field intrinsically is), its
/// <c>IsRequired</c> / <c>IsSearchable</c> flags, and the value in the document's own bag. Vault Extract
/// binds a field to exactly one document type, so the flags live on the definition rather than in a
/// separate per-usage object (#558 non-goal).
/// </para>
/// <para>
/// Registered by convention: ABP exposes a class as any interface whose name (less the leading "I") the
/// class name ends with, and this ends with <c>FlexFieldProvider</c>, so it resolves as
/// <c>IFlexFieldProvider&lt;Document&gt;</c> with no explicit registration.
/// </para>
/// </summary>
public class DocumentFlexFieldProvider : IFlexFieldProvider<Document>, ITransientDependency
{
    protected IFieldRepository FieldRepository { get; }

    protected IRepository<Document, Guid> DocumentRepository { get; }

    protected ICurrentTenant CurrentTenant { get; }

    private readonly Dictionary<Guid, List<Field>> _fieldsByType = new();

    public DocumentFlexFieldProvider(
        IFieldRepository fieldRepository,
        IRepository<Document, Guid> documentRepository,
        ICurrentTenant currentTenant)
    {
        FieldRepository = fieldRepository;
        DocumentRepository = documentRepository;
        CurrentTenant = currentTenant;
    }

    public virtual async Task<IReadOnlyList<FlexFieldValue>> GetFlexFieldsAsync(
        Document entity,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(entity, nameof(entity));

        // An unclassified document has no type, and type-bound fields are the only kind there are - so it
        // genuinely has no fields, rather than an unknown set. Returning empty is correct here and is not
        // the same as the mismatch case below.
        if (entity.DocumentTypeId == null)
        {
            return Array.Empty<FlexFieldValue>();
        }

        // Fail closed on a tenant mismatch rather than resolving fields from the wrong layer.
        //
        // The field query runs under ABP's ambient IMultiTenant filter, so a document from another layer
        // would silently resolve that layer's fields - or, far worse for a rebuild, none at all. "No
        // fields" is indistinguishable from "this document legitimately has none", and the kernel acts on
        // it by deleting the document's index rows. Background paths (extraction, rebuild) are already
        // required to ICurrentTenant.Change(...) first; this turns forgetting into a loud failure instead
        // of quiet data loss.
        if (entity.TenantId != CurrentTenant.Id)
        {
            throw new AbpException(
                $"Cannot resolve flex fields for document {entity.Id}: it belongs to tenant " +
                $"{entity.TenantId?.ToString() ?? "<host>"} but the ambient tenant is " +
                $"{CurrentTenant.Id?.ToString() ?? "<host>"}. Change the ambient tenant before resolving " +
                "a document's fields.");
        }

        // Cached per document type for this provider instance. The kernel calls this once per entity, so a
        // rebuild over N documents would otherwise issue N identical queries for the same handful of field
        // lists. The class is transient, and the index manager holds one instance for the whole rebuild, so
        // the cache lives exactly as long as one operation - keep it transient or this turns into a stale
        // read on the pipeline's per-write path.
        if (!_fieldsByType.TryGetValue(entity.DocumentTypeId.Value, out var fields))
        {
            fields = await FieldRepository.GetListAsync(entity.DocumentTypeId.Value, cancellationToken);
            _fieldsByType[entity.DocumentTypeId.Value] = fields;
        }

        var values = new List<FlexFieldValue>(fields.Count);
        foreach (var field in fields)
        {
            values.Add(new FlexFieldValue(
                field.ToFlexFieldData(),
                required: field.IsRequired,
                searchable: field.IsSearchable,
                value: entity.GetField(field.Name)));
        }

        return values;
    }

    /// <summary>
    /// Pages through every document, for <c>IFlexFieldIndexManager.RebuildAsync</c>.
    /// <para>
    /// Ordered by id because the kernel requires the ordering to be stable across calls: paging by an
    /// unstable order silently skips rows as they shift between pages, and a rebuild that skips rows
    /// produces exactly the failure the index exists to prevent — a document no search finds.
    /// </para>
    /// <para>
    /// This loads whole documents, <c>Markdown</c> included, because the kernel's seam is typed to the
    /// entity. Acceptable at this deployment's scale; if the corpus ever grows enough for that to matter,
    /// the fix is a projection inside the kernel's seam, not a narrower query here that would no longer
    /// satisfy it.
    /// </para>
    /// </summary>
    public virtual async Task<IReadOnlyList<Document>> GetPagedEntitiesAsync(
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        return await DocumentRepository.GetPagedListAsync(
            skipCount,
            maxResultCount,
            sorting: nameof(Document.Id),
            includeDetails: false,
            cancellationToken: cancellationToken);
    }
}

using System.Linq;
using Dignite.Vault.Extract.Documents;
using Microsoft.EntityFrameworkCore;

namespace Dignite.Vault.Extract;

public static class VaultExtractEntityFrameworkCoreQueryableExtensions
{
    public static IQueryable<Document> IncludeDetails(
        this IQueryable<Document> queryable,
        bool include = true)
    {
        // No child collection is eager-loaded here (#593: v2's ExtractedFieldValues collection is gone; v3's
        // FlexFields is a plain JSON column on Document itself and needs no Include). The #527
        // FieldValidationWarnings child is deliberately NOT co-loaded here either: IncludeDetails feeds the
        // list / generic read paths, where a collection Include would reintroduce the #206 Cartesian product
        // across many documents. Warnings are loaded only where the aggregate reconciles / clears them, by
        // FindWithFieldValuesAsync (single-document scope); the review-queue list projects a bounded warning
        // summary instead of hydrating the collection.
        // PipelineRuns are no longer eager-loaded here since #216 split them into an independent aggregate
        // root; queries go through IDocumentPipelineRunRepository.
        return queryable;
    }
}

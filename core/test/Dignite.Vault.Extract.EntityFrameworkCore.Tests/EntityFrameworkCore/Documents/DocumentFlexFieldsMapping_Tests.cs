using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace Dignite.Vault.Extract.EntityFrameworkCore.Documents;

/// <summary>
/// EF mapping for the field architecture v3 value bag (#558): <see cref="Document.FlexFields"/> as a
/// single JSON column, and the stored default that makes adding it to a populated table safe.
/// </summary>
public class DocumentFlexFieldsMapping_Tests : VaultExtractEntityFrameworkCoreTestBase
{
    private readonly IDbContextProvider<VaultExtractDbContext> _dbContextProvider;

    public DocumentFlexFieldsMapping_Tests()
    {
        _dbContextProvider = GetRequiredService<IDbContextProvider<VaultExtractDbContext>>();
    }

    /// <summary>
    /// The column default must be the empty JSON object, never the empty string.
    /// <para>
    /// This guards a mistake that cannot be caught by any behavioural test, because it only harms rows
    /// that existed <i>before</i> the column did. EF defaults a non-nullable string column to <c>""</c>,
    /// which is the CLR default — harmless when the column is born with its table, but this one is added
    /// to a populated <c>VaultDocuments</c>. The bag is read back through
    /// <c>AbpJsonValueConverter</c>, and an empty string is not JSON, so every pre-v3 document would
    /// throw "The input does not contain any JSON tokens" on its next load — at read time, long after the
    /// migration reported success.
    /// </para>
    /// <para>
    /// Asserted against the model rather than the migration file because the model is what regenerating a
    /// migration reads: drop the <c>HasDefaultValueSql</c> from the mapping and the next
    /// <c>dotnet ef migrations add</c> silently goes back to <c>""</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Value_bag_column_defaults_to_an_empty_json_object()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();

            var property = dbContext.Model
                .FindEntityType(typeof(Document))!
                .GetProperties()
                .Single(p => p.Name == nameof(Document.FlexFields));

            var defaultSql = property.GetDefaultValueSql();

            defaultSql.ShouldNotBeNullOrWhiteSpace();
            // Quoting is the provider's business; what matters is that the value is an empty JSON object.
            defaultSql!.Replace("'", string.Empty).Trim().ShouldBe("{}");
        });
    }

    /// <summary>
    /// <c>Field.Description</c> carries what v2 called <c>Prompt</c>, which #447 deliberately left
    /// uncapped as admin-authored configuration. The FlexFields kernel's own mapping still applies a
    /// 256-character limit as of the pinned 10.0.0-rc.5, so Vault Extract clears it — and the entity
    /// enforces no length of its own, which means a regression here would not surface until the database
    /// rejected a long instruction at save time.
    /// </summary>
    [Fact]
    public async Task Field_description_is_not_length_capped()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();

            var property = dbContext.Model
                .FindEntityType(typeof(Dignite.Vault.Extract.Documents.Fields.Field))!
                .GetProperties()
                .Single(p => p.Name == nameof(Dignite.Vault.Extract.Documents.Fields.Field.Description));

            property.GetMaxLength().ShouldBeNull();
        });
    }

    /// <summary>
    /// The exact failure the default prevents, pinned so the reasoning above is not just a comment: this
    /// is what a pre-v3 row would have done on every read had the column been backfilled with "".
    /// </summary>
    [Fact]
    public void An_empty_string_is_not_a_readable_bag()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<FlexFieldDictionary>(string.Empty));

        JsonSerializer.Deserialize<FlexFieldDictionary>("{}")!.Count.ShouldBe(0);
    }
}

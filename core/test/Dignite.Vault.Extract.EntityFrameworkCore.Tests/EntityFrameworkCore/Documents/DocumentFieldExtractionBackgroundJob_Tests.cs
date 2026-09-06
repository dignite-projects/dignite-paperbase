using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Vault.Extract.Ai;
using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.DocumentTypes;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Table;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;
using Dignite.Vault.Extract.Documents.Pipelines;
using Dignite.Vault.Extract.Documents.Pipelines.FieldExtraction;
using Dignite.Vault.Extract.FlexFields;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;
using Xunit;

namespace Dignite.Vault.Extract.EntityFrameworkCore.Documents;

[DependsOn(typeof(VaultExtractEntityFrameworkCoreTestModule))]
public class FieldExtractionJobTestModule : AbpModule
{
    /// <summary>#491: a small ceiling keeps the oversized-body test from persisting a 200k-char body through real EF.</summary>
    public const int MarkdownCeiling = 64;

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IBackgroundJobManager>());

        // IDocumentAppService's wider constructor graph needs a blob container even though the Table
        // egress test below never reads or writes blob content — same substitute other test modules use
        // for the same reason (e.g. FieldTypeCatalogAndSearchabilityTestModule).
        context.Services.AddSingleton(Substitute.For<IBlobContainer<VaultExtractDocumentContainer>>());

        Configure<VaultExtractBehaviorOptions>(options =>
        {
            options.MaxFieldExtractionMarkdownLength = MarkdownCeiling;
        });

        // Register the stub workflow instance directly. ForPartsOf uses the real constructor; DI takes this
        // singleton and bypasses keyed IChatClient resolution.
        var table = new TableFieldTypeExtension();
        var fieldTypeRegistry = new VaultExtractFieldTypeRegistry(new IVaultExtractFieldTypeExtension[]
        {
            new TextFieldTypeExtension(), new NumberFieldTypeExtension(), new BooleanFieldTypeExtension(),
            new DateTimeFieldTypeExtension(), new SelectFieldTypeExtension(), new CKEditorFieldTypeExtension(),
            new TagsFieldTypeExtension(), table
        });
        table.Registry = fieldTypeRegistry;
        var workflow = Substitute.ForPartsOf<FieldExtractionWorkflow>(
            Substitute.For<IChatClient>(),
            NullLogger<FieldExtractionWorkflow>.Instance,
            new FieldSchemaPromptBudgetGuard(Options.Create(new VaultExtractBehaviorOptions())),
            fieldTypeRegistry);
        context.Services.AddSingleton(workflow);
    }
}

/// <summary>
/// UoW-boundary regression tests for <see cref="DocumentFieldExtractionBackgroundJob"/>, required by
/// <c>.claude/rules/background-jobs.md</c> "Tests": external LLM calls
/// (<see cref="FieldExtractionWorkflow.ExtractAsync"/>) must execute outside any ambient UoW.
/// Also verifies that the field-extraction run is persisted as Succeeded and field values are written end to end
/// through real EF.
/// </summary>
public class DocumentFieldExtractionBackgroundJob_Tests
    : VaultExtractTestBase<FieldExtractionJobTestModule>
{
    private readonly DocumentFieldExtractionBackgroundJob _job;
    private readonly FieldExtractionWorkflow _workflow;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IFieldRepository _fieldRepository;
    private readonly IDocumentPipelineRunRepository _runRepository;
    private readonly IGuidGenerator _guidGenerator;

    public DocumentFieldExtractionBackgroundJob_Tests()
    {
        _job = GetRequiredService<DocumentFieldExtractionBackgroundJob>();
        _workflow = GetRequiredService<FieldExtractionWorkflow>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _documentTypeRepository = GetRequiredService<IDocumentTypeRepository>();
        _fieldRepository = GetRequiredService<IFieldRepository>();
        _runRepository = GetRequiredService<IDocumentPipelineRunRepository>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task Runs_LLM_Outside_Ambient_UoW_And_Persists_Succeeded_Run_With_Fields()
    {
        var typeId = _guidGenerator.Create();
        var fieldId = _guidGenerator.Create();
        var documentId = _guidGenerator.Create();

        await WithUnitOfWorkAsync(async () =>
        {
            await _documentTypeRepository.InsertAsync(
                new DocumentType(typeId, null, "type.a", "Type A"), autoSave: true);
            await _fieldRepository.InsertAsync(
                new Field(
                    fieldId, null, typeId, "amount", "Amount",
                    NumberFieldType.ControlName, description: "extract"),
                autoSave: true);

            var doc = NewDocument(documentId);
            doc.SetMarkdown("# Body\n\nAmount: 1500");
            doc.ApplyAutomaticClassificationResult(typeId, 0.99);
            await _documentRepository.InsertAsync(doc, autoSave: true);
        });

        // Core assertion: the LLM call occurs outside any ambient UoW, matching the background-jobs.md short-UoW
        // hard constraint.
        _workflow
            .ExtractAsync(Arg.Any<IReadOnlyList<FieldExtractionDescriptor>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _unitOfWorkManager.Current.ShouldBeNull();
                return new FieldExtractionWorkflowResult(
                    new Dictionary<string, JsonElement?> { ["amount"] = JsonDocument.Parse("1500").RootElement },
                    Array.Empty<FieldValidationWarningResult>());
            });

        // PipelineRunId=null: bulk-path shape. The job creates its own field-extraction run through StartAsync.
        await _job.ExecuteAsync(new DocumentFieldExtractionJobArgs { DocumentId = documentId, PipelineRunId = null });

        await _workflow.Received(1).ExtractAsync(
            Arg.Any<IReadOnlyList<FieldExtractionDescriptor>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await WithUnitOfWorkAsync(async () =>
        {
            var run = await _runRepository.FindLatestByDocumentAndCodeAsync(documentId, VaultExtractPipelines.FieldExtraction);
            run.ShouldNotBeNull();
            run!.Status.ShouldBe(PipelineRunStatus.Succeeded);

            var doc = await _documentRepository.FindWithFieldValuesAsync(documentId);
            doc!.FlexFields["amount"].ShouldBe(1500m);

            // Lifecycle-neutral: field-extraction is not a key pipeline and does not advance or roll back the document.
            doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Processing);
        });
    }

    /// <summary>
    /// #491, end to end through real EF: a body over the ceiling makes the job reach a <b>terminal</b> state without an
    /// LLM call. It must not throw — throwing would return the job to the ABP job store, which would re-send the same
    /// oversized body on every retry — and it must leave the blocking <c>FieldExtractionIncomplete</c> reason behind so
    /// the Ready gate withholds the document from downstream.
    /// </summary>
    [Fact]
    public async Task Oversized_Markdown_Completes_The_Run_Without_An_LLM_Call_And_Blocks_Ready()
    {
        var typeId = _guidGenerator.Create();
        var fieldId = _guidGenerator.Create();
        var documentId = _guidGenerator.Create();

        await WithUnitOfWorkAsync(async () =>
        {
            await _documentTypeRepository.InsertAsync(
                new DocumentType(typeId, null, "type.b", "Type B"), autoSave: true);
            await _fieldRepository.InsertAsync(
                new Field(
                    fieldId, null, typeId, "amount", "Amount",
                    NumberFieldType.ControlName, description: "extract"),
                autoSave: true);

            var doc = NewDocument(documentId);
            doc.SetMarkdown(new string('x', FieldExtractionJobTestModule.MarkdownCeiling + 1));
            doc.ApplyAutomaticClassificationResult(typeId, 0.99);
            await _documentRepository.InsertAsync(doc, autoSave: true);
        });

        // Does not throw: the decline is a normal, terminal outcome of the stage, not a fault.
        await _job.ExecuteAsync(new DocumentFieldExtractionJobArgs { DocumentId = documentId, PipelineRunId = null });

        await _workflow.DidNotReceive().ExtractAsync(
            Arg.Any<IReadOnlyList<FieldExtractionDescriptor>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await WithUnitOfWorkAsync(async () =>
        {
            var run = await _runRepository.FindLatestByDocumentAndCodeAsync(documentId, VaultExtractPipelines.FieldExtraction);
            run.ShouldNotBeNull();
            run!.Status.ShouldBe(PipelineRunStatus.Succeeded);

            var doc = await _documentRepository.FindWithFieldValuesAsync(documentId);
            doc!.FlexFields.ShouldBeEmpty();
            doc.ReviewReasons.HasFlag(DocumentReviewReasons.FieldExtractionIncomplete).ShouldBeTrue();
            ReviewReasonPolicy.HasBlocking(doc.ReviewReasons).ShouldBeTrue();
            // #510: a blocking reason (FieldExtractionIncomplete) withholds Ready -> PendingReview, not Processing.
            doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.PendingReview);
        });
    }

    /// <summary>
    /// #625 end-to-end: a Table field through the REAL pipeline path — stubbed LLM response (a JSON array
    /// of row objects) -&gt; <see cref="FieldExtractionService"/>'s schema application and in-flight guards
    /// -&gt; <c>Document.SetFlexFields</c> -&gt; index sync -&gt; the value appearing correctly in
    /// <see cref="IDocumentAppService.GetAsync"/>'s <c>ExtractedFields</c> egress, all through real EF and
    /// the real DI-registered <c>IVaultExtractFieldTypeRegistry</c> (not the hand-built test registry the
    /// substituted <see cref="FieldExtractionWorkflow"/> above carries — that one is unused here, because
    /// only <c>ExtractAsync</c> is stubbed and everything downstream of it runs for real).
    /// </summary>
    [Fact]
    public async Task Runs_A_Table_Field_Through_The_Real_Pipeline_And_The_Value_Reaches_Egress()
    {
        var typeId = _guidGenerator.Create();
        var fieldId = _guidGenerator.Create();
        var documentId = _guidGenerator.Create();

        var columns = new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName, Required = true },
                new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = NumberFieldType.ControlName }
            }
        }.ConfigurationDictionary;

        await WithUnitOfWorkAsync(async () =>
        {
            await _documentTypeRepository.InsertAsync(
                new DocumentType(typeId, null, "type.table", "Type Table"), autoSave: true);
            await _fieldRepository.InsertAsync(
                new Field(
                    fieldId, null, typeId, "line_items", "Line items",
                    TableFieldType.ControlName, description: "extract line items", configuration: columns,
                    isSearchable: false),
                autoSave: true);

            var doc = NewDocument(documentId);
            doc.SetMarkdown("# Invoice\n\nWidget x3, Gadget x1");
            doc.ApplyAutomaticClassificationResult(typeId, 0.99);
            await _documentRepository.InsertAsync(doc, autoSave: true);
        });

        _workflow
            .ExtractAsync(Arg.Any<IReadOnlyList<FieldExtractionDescriptor>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new FieldExtractionWorkflowResult(
                new Dictionary<string, JsonElement?>
                {
                    ["line_items"] = JsonDocument.Parse(
                        """[{"values":{"item":"Widget","qty":3}},{"values":{"item":"Gadget","qty":1}}]""").RootElement
                },
                Array.Empty<FieldValidationWarningResult>()));

        await _job.ExecuteAsync(new DocumentFieldExtractionJobArgs { DocumentId = documentId, PipelineRunId = null });

        await WithUnitOfWorkAsync(async () =>
        {
            // Reloaded from the DB in a fresh UoW: the bag's JSON column comes back as a JsonElement holding
            // the KERNEL's own List<TableRow> storage shape (each row's cells nested under "values"), not
            // the flat egress shape WriteJson/AssembleExtractedFields produce below - see
            // TableFieldTypeExtension.ReadStoredRows's own doc for this CLR/JsonElement shape split. The
            // per-cell round trip is already pinned at the unit level (TableFieldTypeExtension_Tests); this
            // only confirms the value survived the real persistence round trip, under the right key, with
            // the right row count.
            var doc = await _documentRepository.FindWithFieldValuesAsync(documentId);
            var stored = doc!.FlexFields["line_items"].ShouldBeOfType<JsonElement>();
            stored.ValueKind.ShouldBe(JsonValueKind.Array);
            stored.GetArrayLength().ShouldBe(2);
        });

        // Egress: through the real DocumentAppService.AssembleExtractedFields, not a hand-built value.
        var documentAppService = GetRequiredService<IDocumentAppService>();
        await WithUnitOfWorkAsync(async () =>
        {
            var dto = await documentAppService.GetAsync(documentId);
            dto.ExtractedFields.ShouldNotBeNull();
            dto.ExtractedFields!.ShouldContainKey("line_items");

            var rendered = dto.ExtractedFields["line_items"];
            rendered.ValueKind.ShouldBe(JsonValueKind.Array);
            rendered[0].GetProperty("values").GetProperty("item").GetString().ShouldBe("Widget");
            rendered[0].GetProperty("values").GetProperty("qty").GetDecimal().ShouldBe(3m);
            rendered[1].GetProperty("values").GetProperty("item").GetString().ShouldBe("Gadget");
            rendered[1].GetProperty("values").GetProperty("qty").GetDecimal().ShouldBe(1m);
        });
    }

    private static Document NewDocument(Guid id) =>
        new(
            id,
            tenantId: null,
            fileOrigin: new FileOrigin(
                blobName: $"blobs/{id:N}.pdf",
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
                contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                fileSize: 1024,
                originalFileName: "test.pdf"));
}

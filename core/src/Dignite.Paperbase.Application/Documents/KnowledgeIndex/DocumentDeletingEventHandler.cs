using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents.Events;
using Dignite.Paperbase.KnowledgeIndex;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.Uow;

namespace Dignite.Paperbase.Documents.KnowledgeIndex;

public class DocumentDeletingEventHandler :
    ILocalEventHandler<DocumentDeletingEvent>,
    ITransientDependency
{
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IDocumentKnowledgeIndex _documentKnowledgeIndex;
    private readonly ILogger<DocumentDeletingEventHandler> _logger;

    public DocumentDeletingEventHandler(
        IUnitOfWorkManager unitOfWorkManager,
        IDocumentKnowledgeIndex documentKnowledgeIndex,
        ILogger<DocumentDeletingEventHandler> logger)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _documentKnowledgeIndex = documentKnowledgeIndex;
        _logger = logger;
    }

    public virtual Task HandleEventAsync(DocumentDeletingEvent eventData)
    {
        var currentUnitOfWork = _unitOfWorkManager.Current;
        if (currentUnitOfWork == null)
        {
            _logger.LogWarning(
                "Document {DocumentId} delete event was handled without an active unit of work; knowledge index cleanup was skipped.",
                eventData.DocumentId);
            return Task.CompletedTask;
        }

        var documentId = eventData.DocumentId;
        var tenantId = eventData.TenantId;

        currentUnitOfWork.OnCompleted(async () =>
        {
            try
            {
                await _documentKnowledgeIndex.DeleteByDocumentIdAsync(documentId, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to delete document {DocumentId} from the knowledge index after the document transaction committed.",
                    documentId);
            }
        });

        return Task.CompletedTask;
    }
}

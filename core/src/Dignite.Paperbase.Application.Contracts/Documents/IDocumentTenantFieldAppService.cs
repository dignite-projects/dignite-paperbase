using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Dignite.Paperbase.Documents;

public interface IDocumentTenantFieldAppService : IApplicationService
{
    Task<List<DocumentTenantFieldDto>> GetByDocumentAsync(Guid documentId);
}

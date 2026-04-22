using System;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Contracts.Contracts;

public interface IContractRepository : IRepository<Contract, Guid>
{
    Task<Contract?> FindByDocumentIdAsync(Guid documentId);
}

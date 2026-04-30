using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Chat;

public interface IChatConversationRepository : IRepository<ChatConversation, Guid>
{
    /// <summary>
    /// Loads a conversation with its most recent <paramref name="messageTake"/> messages
    /// ordered by CreationTime ASC (tail window).
    /// Returns null if the conversation does not exist.
    /// </summary>
    Task<ChatConversation?> FindByIdWithMessagesAsync(
        Guid id,
        int messageTake,
        CancellationToken cancellationToken = default);
}

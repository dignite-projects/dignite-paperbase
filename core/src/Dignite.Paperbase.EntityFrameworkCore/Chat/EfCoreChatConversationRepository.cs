using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents.Chat;
using Dignite.Paperbase.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Documents.Chat;

public class EfCoreChatConversationRepository
    : EfCoreRepository<PaperbaseDbContext, ChatConversation, Guid>, IChatConversationRepository
{
    public EfCoreChatConversationRepository(IDbContextProvider<PaperbaseDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<ChatConversation?> FindByIdWithMessagesAsync(
        Guid id,
        int messageTake,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var ct = GetCancellationToken(cancellationToken);

        var conversation = await dbContext.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (conversation == null)
            return null;

        // Load the tail window (most recent N messages), then reorder ASC.
        // A separate LoadAsync keeps the window size; EF Core's relationship fix-up then
        // populates the private _messages backing field on the already-tracked conversation.
        // Requires a tracking context (GetDbContextAsync, not AsNoTracking).
        await dbContext.Set<ChatMessage>()
            .Where(m => m.ConversationId == id)
            .OrderByDescending(m => m.CreationTime)
            .Take(messageTake)
            .OrderBy(m => m.CreationTime)
            .LoadAsync(ct);

        return conversation;
    }
}

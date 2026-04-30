using Dignite.Paperbase.Chat;
using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;

namespace Dignite.Paperbase.Chat.Mappers;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ChatConversationToChatConversationDtoMapper
    : MapperBase<ChatConversation, ChatConversationDto>
{
    public override partial ChatConversationDto Map(ChatConversation source);
    public override partial void Map(ChatConversation source, ChatConversationDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ChatConversationToChatConversationListItemDtoMapper
    : MapperBase<ChatConversation, ChatConversationListItemDto>
{
    public override partial ChatConversationListItemDto Map(ChatConversation source);
    public override partial void Map(ChatConversation source, ChatConversationListItemDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ChatMessageToChatMessageDtoMapper
    : MapperBase<ChatMessage, ChatMessageDto>
{
    public override partial ChatMessageDto Map(ChatMessage source);
    public override partial void Map(ChatMessage source, ChatMessageDto destination);
}

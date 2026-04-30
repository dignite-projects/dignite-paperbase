using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Permissions;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Xunit;

namespace Dignite.Paperbase.Permissions;

public class DocumentChatPermissions_Tests : PaperbaseApplicationTestBase<PaperbaseApplicationTestModule>
{
    private readonly IPermissionDefinitionManager _manager;

    public DocumentChatPermissions_Tests()
    {
        _manager = GetRequiredService<IPermissionDefinitionManager>();
    }

    [Theory]
    [InlineData(PaperbasePermissions.Documents.Chat.Default)]
    [InlineData(PaperbasePermissions.Documents.Chat.Create)]
    [InlineData(PaperbasePermissions.Documents.Chat.SendMessage)]
    [InlineData(PaperbasePermissions.Documents.Chat.Delete)]
    public async Task Should_Define_Chat_Permission(string name)
    {
        var def = await _manager.GetOrNullAsync(name);
        def.ShouldNotBeNull();
    }

    [Fact]
    public async Task Chat_Permissions_Should_Be_Children_Of_Documents()
    {
        var documents = await _manager.GetOrNullAsync(PaperbasePermissions.Documents.Default);
        documents.ShouldNotBeNull();

        var chat = documents!.Children.SingleOrDefault(c => c.Name == PaperbasePermissions.Documents.Chat.Default);
        chat.ShouldNotBeNull();

        chat!.Children.Select(c => c.Name).ShouldBe(new[]
        {
            PaperbasePermissions.Documents.Chat.Create,
            PaperbasePermissions.Documents.Chat.SendMessage,
            PaperbasePermissions.Documents.Chat.Delete,
        }, ignoreOrder: true);
    }
}

using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Permissions;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Xunit;

namespace Dignite.Paperbase.Permissions;

public class ChatPermissions_Tests : PaperbaseApplicationTestBase<PaperbaseApplicationTestModule>
{
    private readonly IPermissionDefinitionManager _manager;

    public ChatPermissions_Tests()
    {
        _manager = GetRequiredService<IPermissionDefinitionManager>();
    }

    [Theory]
    [InlineData(PaperbasePermissions.Chat.Default)]
    [InlineData(PaperbasePermissions.Chat.Create)]
    [InlineData(PaperbasePermissions.Chat.SendMessage)]
    [InlineData(PaperbasePermissions.Chat.Delete)]
    public async Task Should_Define_Chat_Permission(string name)
    {
        var def = await _manager.GetOrNullAsync(name);
        def.ShouldNotBeNull();
    }

    [Fact]
    public async Task Chat_Permissions_Should_Be_Top_Level_Group_Children()
    {
        var chat = await _manager.GetOrNullAsync(PaperbasePermissions.Chat.Default);
        chat.ShouldNotBeNull();

        chat!.Children.Select(c => c.Name).ShouldBe(new[]
        {
            PaperbasePermissions.Chat.Create,
            PaperbasePermissions.Chat.SendMessage,
            PaperbasePermissions.Chat.Delete,
        }, ignoreOrder: true);
    }
}

using System.Linq;
using Microsoft.Agents.AI;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Contracts.Chat;

/// <summary>
/// Issue #149 follow-up: guard the ABP DI wiring that exposes the three contract
/// skills to <c>ChatAppService</c>. The skills rely on
/// <c>[ExposeServices(typeof(AgentSkill))]</c> + <c>ITransientDependency</c> on each
/// <see cref="AgentClassSkill{TSelf}"/> subclass so ABP auto-registers them as
/// <see cref="AgentSkill"/>. If a future refactor drops one of those attributes (or
/// switches the base class), this test fails loudly — without it, the application
/// would still compile and chat would still run; only contract questions would
/// silently degrade because the skill never reached the agent's <c>AgentSkillsProvider</c>.
/// </summary>
public class ContractSkillRegistration_Tests
    : ContractsApplicationTestBase<ContractsApplicationTestModule>
{
    [Fact]
    public void ContractsApplicationModule_Registers_All_Three_Skills_As_AgentSkill()
    {
        var skills = GetRequiredService<System.Collections.Generic.IEnumerable<AgentSkill>>().ToList();
        var names = skills.Select(s => s.Frontmatter.Name).ToHashSet();

        names.ShouldContain("search-contracts");
        names.ShouldContain("get-contract-detail");
        names.ShouldContain("aggregate-contracts");
    }

    [Fact]
    public void Registered_AgentSkills_Are_Distinct_Instances_Of_Each_Type()
    {
        // Resolving as IEnumerable<AgentSkill> must return one instance per skill class
        // — not the same instance returned multiple times under different facades. A
        // mis-registration ("AddSingleton<AgentSkill, SearchContractsSkill>()" only)
        // would collapse the inventory to one entry; we want three.
        var skills = GetRequiredService<System.Collections.Generic.IEnumerable<AgentSkill>>().ToList();

        skills.Count.ShouldBeGreaterThanOrEqualTo(3);
        // Class-based skill instances are distinct types — the runtime types must include
        // all three concrete subclasses.
        var runtimeTypes = skills.Select(s => s.GetType()).ToHashSet();
        runtimeTypes.ShouldContain(typeof(SearchContractsSkill));
        runtimeTypes.ShouldContain(typeof(GetContractDetailSkill));
        runtimeTypes.ShouldContain(typeof(AggregateContractsSkill));
    }
}

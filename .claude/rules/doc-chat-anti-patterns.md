# doc-chat 反例说明

本文件由 `maf-workflow-reviewer` agent 在审查 PR 时引用，用于快速定位 **DocumentChatAppService** 发送路径
和业务模块 **字段抽取 Agent** 的典型错误模式。

所有示例均为**伪代码**，不可编译，仅用于说明意图。

---

## 反例 A：业务模块字段抽取 Agent 挂 AIContextProviders

**规则来源**：`maf-workflow-reviewer.md § 2.9 规则 A`

### ❌ 错误写法

```
// 错误：给字段抽取 agent 挂 TextSearchProvider（RAG 检索）
var provider = new TextSearchProvider(
    async (query, ct) => /* fetch chunks from your vector store */,
    options: /* TextSearchProviderOptions */);
var options = new ChatClientAgentOptions
{
    AIContextProviders = [provider],          // ← 禁止
    ChatHistoryProvider = new InMemory...()   // ← 禁止
};
var agent = new ChatClientAgent(_chatClient, options);
var run = await agent.RunAsync<ContractExtractionResult>(extractedText);
```

**危害**：
- RAG 检索会把与当前文档无关的其他文档 chunk 注入到 prompt 中，导致
  "合同金额"、"甲方名称"等结构化字段从错误文档提取，写入业务聚合根
- 业务模块与 Core RAG 管道产生隐式耦合，违反模块独立性（modules/ 不依赖 Core Application 内部）

### ✅ 正确写法

```
// 正确：仅 IChatClient + system instructions，RunAsync<T> 结构化输出
var agent = new ChatClientAgent(
    _chatClient,
    instructions: ContractAgentInstructions.SystemPrompt);
var run = await agent.RunAsync<ContractExtractionResult>(extractedText);
return run.Result ?? new ContractExtractionResult();
```

**参照实现**：
`modules/contracts/src/Dignite.Paperbase.Contracts.Domain/EventHandlers/ContractDocumentHandler.ExtractFieldsAsync`

---

## 反例 B：削弱 SendMessageAsync 的 fail-closed 安全门

**规则来源**：`maf-workflow-reviewer.md § 2.10`

### ❌ 错误写法 1：跳过租户断言

```
// 错误：以"ambient filter 已过滤"为由删除显式租户断言
var conversation = await _conversationRepository.FindAsync(conversationId);
if (conversation == null) throw new EntityNotFoundException(...);
// 缺少 conversation.TenantId != CurrentTenant.Id 的显式检查
// 如果 DataFilter 被测试或特殊代码路径 Disable，则跨租户访问无声通过
```

**危害**：ambient 数据过滤器（`DataFilter`）是可读性辅助，不是安全边界。任何禁用过滤器的路径
（后台任务、框架升级或非预期代码路径）都会绕过保护。

### ❌ 错误写法 2：以内容 hash 作幂等键

```
// 错误：用消息内容而非 ClientTurnId 做幂等判断
var existingMessage = conversation.Messages
    .FirstOrDefault(m => m.Role == User && m.Content == input.Message);
if (existingMessage != null) return BuildTurnResult(existingMessage);
```

**危害**：相同内容但不同意图的两条消息（用户重新提问）会被误认为重复，
导致第二条消息静默丢弃，用户看不到任何错误。

### ❌ 错误写法 3：捕获并静默重试并发异常

```
// 错误：捕获 AbpDbConcurrencyException 并重试
try
{
    await _conversationRepository.UpdateAsync(conversation, autoSave: true);
}
catch (AbpDbConcurrencyException)
{
    // 静默重试，不让客户端知道发生了并发冲突
    await _conversationRepository.UpdateAsync(conversation, autoSave: true);
}
```

**危害**：客户端无法感知并发冲突（409），无法决策是否重试或合并。
正确做法是让异常冒泡，ABP HTTP 层将其映射为 409 Conflict。

### ✅ 正确实现要点

```
[Authorize(PaperbasePermissions.Documents.Chat.SendMessage)]
public virtual async Task<ChatTurnResultDto> SendMessageAsync(Guid conversationId, SendChatMessageInput input)
{
    // 1. LoadAndAuthorizeAsync 依次: 租户断言 → 归属断言（均抛 EntityNotFoundException）
    var conversation = await LoadAndAuthorizeAsync(conversationId, includeMessages: true);

    // 2. ClientTurnId 幂等短路（命中则不再调用 LLM）
    var existing = conversation.Messages.FirstOrDefault(
        m => m.Role == ChatMessageRole.User && m.ClientTurnId == input.ClientTurnId);
    if (existing != null) return BuildTurnResultFromPersisted(conversation, existing);

    // 3. 调用 Agent...

    // 4. AbpDbConcurrencyException 不在此捕获，让其冒泡为 409
}
```

**参照实现**：
`core/src/Dignite.Paperbase.Application/Documents/Chat/DocumentChatAppService.cs`

---

## 反例 C：业务模块 `IDocumentChatToolContributor` 工具未做 fail-closed 安全门

**规则来源**：Issue #69 验收标准 — "每个 tool 显式断言租户 + 权限"

**背景**：业务模块通过 `IDocumentChatToolContributor` 把 `AIFunction` 挂进 Chat 后，函数体由 LLM 决定何时调用、参数由 LLM 决定如何填。HTTP 边界上的 `[Authorize]` 不再覆盖此调用——AIFunction 在 Chat 转一轮内被反射调用，绕过 controller。安全断言必须落到工具方法体内部。

### ❌ 错误写法 1：依赖 AppService 上的 `[Authorize]` / 不做权限断言

```
public class InvoiceChatToolContributor : IDocumentChatToolContributor, ITransientDependency
{
    public IEnumerable<AIFunction> ContributeTools(DocumentChatToolContext ctx)
    {
        // 错误：直接把 AppService 方法包成 AIFunction
        // IInvoiceAppService 的 [Authorize(InvoicePermissions.Default)] 在反射调用时不生效
        yield return AIFunctionFactory.Create(_invoiceAppService.GetListAsync,
            name: "search_invoices", description: "...");
    }
}
```

**危害**：仅持有 Chat 权限的用户通过自然语言（"帮我查张三的发票"）即可拿到本无权访问的发票数据。LLM 是无意识的"权限提升通道"。

### ❌ 错误写法 2：依赖 ABP `DataFilter` 做租户隔离

```
public async Task<string> SearchAsync(string keyword)
{
    await _authService.CheckAsync(InvoicePermissions.Default);
    var queryable = await _repo.GetQueryableAsync();   // ← ambient filter 自动按 CurrentTenant.Id 过滤
    return JsonSerializer.Serialize(await _executer.ToListAsync(queryable.Where(...).Take(20)));
}
```

**危害**：与反例 B 错误写法 1 同源——任何禁用 `DataFilter` 的代码路径（后台任务、非 HTTP 上下文、单元测试 helper）会让此工具跨租户返回数据。

### ❌ 错误写法 3：结果集无上限

```
var matches = await _executer.ToListAsync(queryable.Where(c => c.PartyName.Contains(name)));
return JsonSerializer.Serialize(matches);   // ← 命中 5000 条全返回
```

**危害**：单次 tool 调用炸 LLM context window；攻击者可通过宽泛 keyword 制造内存压力或费用攻击。

### ❌ 错误写法 4：把用户输入拼进工具描述

```
yield return AIFunctionFactory.Create(binding.SearchAsync,
    name: "search_invoices",
    description: $"Search invoices belonging to user {ctx.UserDisplayName}. ...");
```

**危害**：description 文本是 LLM 决策上下文的一部分。如果 `UserDisplayName` 来自用户控制的字段（昵称、签名），可被用作 prompt injection 注入向量。description 必须是**编译期常量**或纯静态文本。

### ❌ 错误写法 5：裸跑 raw SQL

```
public async Task<string> ReportAsync(string whereClause)
{
    var sql = $"SELECT * FROM Invoices WHERE {whereClause}";   // ← LLM 拼 SQL
    return await _dbContext.Database.SqlQueryRaw<...>(sql).ToListAsync();
}
```

**危害**：SQL 注入面 + 绕过 ABP 权限/审计/软删除/租户过滤层。即便是 LLM 生成 SQL 看似可控，也在攻击面内（prompt injection 完全可以诱导 LLM 写 `WHERE 1=1` 或 `; DROP TABLE`）。

### ✅ 正确实现要点

```
public class InvoiceChatToolContributor : IDocumentChatToolContributor, ITransientDependency
{
    public IEnumerable<AIFunction> ContributeTools(DocumentChatToolContext ctx)
    {
        var binding = new InvoiceToolBindings(_repo, _executer, ctx.TenantId, _authService);
        yield return AIFunctionFactory.Create(binding.SearchAsync,
            name: "search_invoices",
            description: "Search invoices by ...");   // ← 静态常量
    }

    private sealed class InvoiceToolBindings
    {
        private const int MaxResultRows = 20;
        // 构造函数注入 _tenantId（来自 DocumentChatToolContext.TenantId）

        public async Task<string> SearchAsync(/* [Description] params */)
        {
            // 1. 显式权限断言 — fail closed
            await _authService.CheckAsync(InvoicePermissions.Default);

            // 2. 显式租户谓词 — 不依赖 ambient DataFilter
            var q = (await _repo.GetQueryableAsync())
                .Where(i => i.TenantId == _tenantId);

            // 3. 业务过滤 + 强制 Take(N)
            var rows = await _executer.ToListAsync(
                q.Where(...).OrderBy(...).Take(MaxResultRows), ct);

            return JsonSerializer.Serialize(new { rows });
        }
    }
}
```

**参照实现**：
`modules/contracts/src/Dignite.Paperbase.Contracts.Application/Chat/ContractChatToolContributor.cs`

# LLM call anti-patterns

本文件由 `maf-workflow-reviewer` agent 在审查 PR 时引用，用于快速定位 Paperbase 内部 **LLM 调用点**的两类典型错误。规则适用于所有 LLM 入口：

- 当前已落地：`DocumentClassificationWorkflow` / `HostFieldExtractionWorkflow` / `TenantFieldExtractionEventHandler` / `DocumentTextExtractionBackgroundJob.TryGenerateTitleAsync`
- 未来扩展：MCP server tool（[#170](https://github.com/dignite-projects/dignite-paperbase/issues/170)）、Webhook 触发的 LLM 路径、任何由 LLM 输出影响参数的查询路径

所有示例均为**伪代码**，不可编译，仅用于说明意图。

---

## 反例 A：字段抽取 Agent 挂 AIContextProviders

**规则来源**：`maf-workflow-reviewer.md § 2.10`

**适用范围**：所有结构化字段抽取路径——分类、Host 字段抽取、租户字段抽取（B 机制），以及未来新增的字段抽取 workflow / agent。

### ❌ 错误写法

```csharp
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
var run = await agent.RunAsync<HostFieldExtractionResult>(markdown);
```

**危害**：

- RAG 检索会把与当前文档无关的其他文档 chunk 注入到 prompt 中，导致"合同金额"、"甲方名称"等结构化字段**从错误文档**提取，写入下游业务聚合根 / Paperbase 类型绑定字段表
- Paperbase 是**通道层**，字段抽取的输入应当**仅是当前文档的 Markdown**——挂 `AIContextProviders` 把通道哲学破坏成 RAG，违反 CLAUDE.md "OUT of scope"

### ✅ 正确写法

```csharp
// 正确：仅 IChatClient + system instructions，RunAsync<T> 结构化输出
var agent = new ChatClientAgent(
    _chatClient,
    instructions: HostFieldExtractionInstructions.SystemPrompt);
var run = await agent.RunAsync<HostFieldExtractionResult>(markdown);
return run.Result ?? new HostFieldExtractionResult();
```

或者像当前 `HostFieldExtractionWorkflow` 那样直接调 `IChatClient.GetResponseAsync`，绕过 agent 封装：

```csharp
// 正确：直接构造 ChatMessage 列表，无 AIContextProvider 干扰
var messages = new List<ChatMessage>
{
    new(ChatRole.System, systemPrompt + "\n\n" + PromptBoundary.BoundaryRule),
    new(ChatRole.User, PromptBoundary.WrapDocument(truncated))
};
var options = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };
var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
```

**参照实现**：
- `core/src/Dignite.Paperbase.Application/Documents/Pipelines/Classification/DocumentClassificationWorkflow.cs`
- `core/src/Dignite.Paperbase.Application/Documents/Pipelines/FieldExtraction/HostFieldExtractionWorkflow.cs`

---

## 反例 B：LLM 触发的查询路径未做 fail-closed 安全门

**规则来源**：`maf-workflow-reviewer.md § 2.9`

**背景**：当前 Paperbase Core 的 LLM 路径都是**单纯的文本输入 → 结构化输出**（分类、字段抽取、标题生成），不涉及由 LLM 输出参数化的 DB 查询。但即将到来的 #170 MCP server 会让 LLM 通过 tool 接口触发文档检索 / 元数据查询。**任何 LLM 触发或参数受 LLM 输出影响的查询路径**都必须做 fail-closed 安全门——HTTP 边界上的 `[Authorize]` 不覆盖这种反射 / tool-dispatch 路径；script / tool 方法体内的安全断言是唯一防线。

**适用范围**：

- 未来的 MCP server tool 方法（#170）
- 未来的 Webhook handler 中触发 LLM 工具调用的路径
- 任何让 LLM 拿到查询参数并落到 `IRepository.GetQueryableAsync()` / `IAsyncQueryableExecuter.ToListAsync()` 的代码点

### ❌ 错误写法 1：依赖 AppService 上的 `[Authorize]` / 不做权限断言

```csharp
[McpTool("search-documents")]
public async Task<string> SearchAsync(string keyword, IServiceProvider sp, CancellationToken ct)
{
    // 错误：直接把 AppService 方法的结果序列化返回
    // IDocumentAppService 的 [Authorize(Documents.Default)] 在 LLM 反射调用时不生效
    var appService = sp.GetRequiredService<IDocumentAppService>();
    var result = await appService.GetListAsync(new GetDocumentListInput { Keyword = keyword });
    return JsonSerializer.Serialize(result);
}
```

**危害**：仅持有 MCP 端点访问凭据的客户端通过自然语言（"帮我查张三的合同"）即可拿到本无权访问的文档。LLM 是无意识的"权限提升通道"。

### ❌ 错误写法 2：依赖 ABP `DataFilter` 做租户隔离

```csharp
public async Task<string> SearchAsync(string keyword)
{
    await _authService.CheckAsync(DocumentPermissions.Default);
    var q = await _repo.GetQueryableAsync();   // ← ambient filter 自动按 CurrentTenant.Id 过滤
    return JsonSerializer.Serialize(await _executer.ToListAsync(q.Where(...).Take(20)));
}
```

**危害**：ambient `DataFilter` 是可读性辅助，不是安全边界。任何禁用 `DataFilter` 的代码路径（后台任务、非 HTTP 上下文、单元测试 helper）会让此工具跨租户返回数据。

### ❌ 错误写法 3：结果集无上限

```csharp
var matches = await _executer.ToListAsync(q.Where(d => d.Title.Contains(keyword)));
return JsonSerializer.Serialize(matches);   // ← 命中 5000 条全返回
```

**危害**：

- 单次 tool 调用炸 LLM context window，后续 turn 退化
- 攻击者通过宽泛 keyword（如 `""`）制造内存压力或费用攻击

### ❌ 错误写法 4：把用户输入拼进 tool description / instructions

```csharp
public override McpToolDescriptor Descriptor { get; } = new(
    "search-documents",
    $"Search documents belonging to user {someUserName}. ...");   // ← 禁止：构造器参数运行时拼

// 或：
protected override string Instructions => $"You serve user {dynamicValue}. ...";
```

**危害**：description / instructions 文本是 LLM 决策上下文的一部分。如果包含用户控制的字符串（昵称、签名、文档名），可被用作 prompt injection 注入向量。Descriptor 文本和 instructions 都必须是**编译期常量**或纯静态字符串字面量。

### ❌ 错误写法 5：裸跑 raw SQL

```csharp
public async Task<string> ReportAsync(string whereClause)
{
    var sql = $"SELECT * FROM Documents WHERE {whereClause}";   // ← LLM 拼 SQL
    return await _dbContext.Database.SqlQueryRaw<...>(sql).ToListAsync();
}
```

**危害**：SQL 注入面 + 绕过 ABP 权限 / 审计 / 软删除 / 租户过滤层。即便是 LLM 生成 SQL 看似可控，也在攻击面内（prompt injection 完全可以诱导 LLM 写 `WHERE 1=1` 或 `; DROP TABLE`）。

### ✅ 正确实现要点

每个 LLM 触发的查询点（无论 MCP tool、Webhook handler、还是其他类似入口）依次满足以下条件：

```csharp
[McpTool("search-documents")]
[Description("Search Paperbase documents by structured criteria.")]   // ← 编译期常量
private static async Task<string> SearchAsync(
    string? keyword,
    [Description("ISO 4217 currency code (optional).")] string? currency,
    IServiceProvider serviceProvider,
    CancellationToken cancellationToken = default)
{
    // 1. 显式权限断言 — fail closed
    var authSvc = serviceProvider.GetRequiredService<IAuthorizationService>();
    await authSvc.CheckAsync(PaperbasePermissions.Documents.Default);

    // 2. 显式租户谓词 — 不依赖 ambient DataFilter
    var currentTenant = serviceProvider.GetRequiredService<ICurrentTenant>();
    var tenantId = currentTenant.Id;
    var repo = serviceProvider.GetRequiredService<IDocumentRepository>();
    var q = (await repo.GetQueryableAsync()).Where(d => d.TenantId == tenantId);

    // 3. 业务过滤 + 强制 Take(N)
    var executer = serviceProvider.GetRequiredService<IAsyncQueryableExecuter>();
    var rows = await executer.ToListAsync(
        q.Where(d => keyword == null || d.Title.Contains(keyword))
         .OrderByDescending(d => d.CreationTime)
         .Take(MaxResultRows),
        cancellationToken);

    // 4. 用户派生自由文本字段必须 PromptBoundary.WrapField(...) 包裹
    return JsonSerializer.Serialize(new
    {
        rows = rows.Select(r => new
        {
            r.Id,
            r.CreationTime,
            title = PromptBoundary.WrapField(r.Title),            // ← 包裹
            documentTypeCode = r.DocumentTypeCode                 // 系统字段，不需要 wrap
        })
    });
}
```

**关键要点回顾**：

1. **`[Authorize]` 不够**——必须在方法体显式 `CheckAsync(Permission)`
2. **`DataFilter` 不是安全边界**——必须显式 `Where(x => x.TenantId == tenantId)`
3. **必须 `Take(N)`**——单次 tool 调用结果集硬上限，防止 prompt-injection 诱导宽泛查询
4. **description / instructions 必须是编译期常量**——禁止拼用户字符串
5. **不允许 raw SQL**——LLM 拼 SQL 在攻击面内
6. **PromptBoundary**——返回值里的用户派生自由文本字段（title、partyName、summary 等）必须经 `PromptBoundary.WrapField(...)` 包裹

---

## 同源安全约定（适用于所有 LLM 路径）

上述两个反例都是 CLAUDE.md "## 安全约定" 一节 4 条 bullet 的具体化：

- **Fail-closed 安全断言**——权限 + 租户 + 上限 + 不裸 SQL
- **PromptBoundary**——用户派生自由文本进 prompt / LLM-facing 输出前必须包裹
- **Description / Instructions 编译期常量**——禁止运行时拼用户字符串
- **多租户隔离**——所有 Document / 衍生字段查询路径显式按 `CurrentTenant.Id` 过滤

新增任何 LLM 调用点时，按这 4 条做 self-review；`maf-workflow-reviewer` agent 在 PR 审查时也按此清单核对。

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
var (provider, _) = _textSearchAdapter.CreateForTenant(tenantId, scope);
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

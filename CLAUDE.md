# Dignite Paperbase

这是一个 ABP 框架的项目，遵循 `.claude/rules/` 中的 ABP 核心约定和模块模板。

## 项目组织

项目分为四个主要目录：
- **core/** - ABP 应用程序核心，遵循 abp-core.md 规则
- **modules/** - 可复用业务模块，每个模块遵循 module-template.md 的结构和虚拟方法要求
- **host/** - 单租户测试主机，仅在此配置中间件（OnApplicationInitialization）
- **docs/** - 面向开发者和使用者的操作/配置/API 文档；设计方案与架构决策走 GitHub Issues，不在 docs/ 下落地

## 架构设计

Paperbase 采用**三层分离**的模块化架构：

### 第一层：Core（基础设施与扩展契约）

`core/` 包含两个核心部分：

1. **Dignite.Paperbase.Abstractions（扩展契约层）**
   - **位置**：依赖拓扑的最底层（无其他 Paperbase 项目依赖）
   - **职责**：参考 `Volo.Abp.Users.Abstractions` 模式，提供业务模块和能力模块接入平台所必需的契约
   - **内容**：
     - 文档类型注册：`DocumentTypeDefinition`、`DocumentTypeOptions`
     - 集成事件：`DocumentClassifiedEto`
     - 文本提取契约（多 OCR Provider 可插拔）：`ITextExtractor`、`TextExtractionContext`、`TextExtractionResult`
   - **约束**：不依赖任何其他 Paperbase 项目，仅依赖 ABP 基础模块

2. **Dignite.Paperbase 核心模块栈**
   - **Domain.Shared / Domain / Application / EntityFrameworkCore / HttpApi**：标准 ABP 分层
   - **核心 AI 能力（分类 / 向量-RAG / 关系推断 / Chat 问答）直接在 Application 层落地**——通过 Microsoft Agent Framework (MAF) 1.0 的 `ChatClientAgent` 实现，不再独立成 AI 模块
   - **能力模块**：`TextExtraction`（默认文本提取）、`Ocr.AzureDocumentIntelligence`（Azure OCR Provider）

### 第二层：Modules（业务模块生态）

`modules/` 中的各业务模块（如 Contracts）：

- **依赖关系**：依赖 `Abstractions`（注册类型、订阅事件）+ ABP 基础模块；如需调用 LLM，直接 NuGet 引用 `Microsoft.Extensions.AI` + `Microsoft.Agents.AI`
- **职责**：
  - 通过 `DocumentTypeOptions` 注册自己关心的文档类型
  - 通过 `IDistributedEventBus` 订阅 `DocumentClassifiedEto` 事件
  - **使用 MAF `ChatClientAgent` + 结构化输出**自实现领域专属的字段提取（不再有通用的 `IFieldExtractor` 抽象）
  - 持久化自己的领域聚合根，提供业务 API 和 UI
  - 在自己的聚合根上维护业务查询字段，**不得回写到 Document 聚合根**
- **非耦合实现**：业务模块之间无依赖；业务模块与核心通过事件解耦通信

### 第三层：Host（宿主应用）

`host/` 仅作为容器：

- 在 `[DependsOn(...)]` 中声明依赖的核心模块、能力模块和业务模块
- 在 `ConfigureServices()` 中：
  - 配置 OCR Provider（如 Azure Document Intelligence）
  - 注册 `IChatClient` + `IEmbeddingGenerator<string, Embedding<float>>`（Azure OpenAI 或 Ollama）——所有上层 MAF Agent 共享这套 LLM 接入
- **仅在此处配置中间件**（`OnApplicationInitialization`）
- 不实现任何业务逻辑

### 依赖流向

```
Host Application
    ├── 注册 IChatClient + IEmbeddingGenerator
    └── DependsOn:
        ├── Dignite.Paperbase.Application（核心 + 内嵌 MAF Workflow）
        ├── Dignite.Paperbase.TextExtraction（能力）
        ├── Dignite.Paperbase.Ocr.AzureDocumentIntelligence（能力）
        └── Dignite.Paperbase.Contracts.Application（业务模块）

Dignite.Paperbase.Abstractions（扩展契约层，无其他项目依赖）
    ├── DocumentTypeDefinition / DocumentTypeOptions / DocumentClassifiedEto
    ├── ITextExtractor + POCO（OCR Provider 实现侧）
    └── 被业务模块订阅事件、注册类型
```

**核心约束**：
- **单向依赖**：Abstractions 处于最底层，被所有上层引用
- **业务模块间无耦合**：每个业务模块独立开发、独立测试、可独立卸载
- **编排在 Application**：BackgroundJob、Workflow、PipelineRun 生命周期、Document 读写均在 Paperbase.Application

### Document 聚合根边界（强制）

`Document` 是**纯基础设施聚合根**，职责限于：文件存储、生命周期状态机、流水线 Run 记录、文本提取结果、AI 分类结果、向量化状态。

**禁止**在 `Document` 上添加任何来自业务模块的字段，例如合同金额、到期日、对方名称、发票号等。这类字段属于业务模块自己的聚合根，由业务模块在收到 `DocumentClassifiedEto` 后自行持久化和查询。

> **判断依据**：如果一个字段的含义只有在特定业务场景（合同、发票、报销单…）下才成立，它就不属于 `Document`。

### Markdown-first 数据流（强制）

项目定位是 **AI 驱动的企业档案平台**，遇到取舍时优先 AI 友好的设计。Markdown 是 AI pipeline 的**唯一文本载荷**：

- **OCR / 数字版抽取**：`ITextExtractor` / `IMarkdownTextProvider` / `IOcrProvider` 实现方**必须**输出 Markdown（标题、表格、列表是向量化切块和 LLM 理解的关键语义信号）；即使源文件无结构，也以扁平 Markdown 段落输出，**不得**退回 plain text 路径。
- **持久化**：`Document.Markdown` 是 Document 聚合根上唯一的文本字段，**禁止**在 `Document` 或事件载荷（如 `DocumentClassifiedEto`）上引入并行的 plain-text 字段。
- **下游消费**：向量化（`TextChunker` 按 Markdown AST 切块 + 注入 header path）、LLM 分类 / QA / Rerank、业务模块字段抽取，统一消费 Markdown。
- **纯文本投影**：仅在消费侧（如关键字兜底分类器）按需通过 `Dignite.Paperbase.Documents.MarkdownStripper.Strip(...)` 即时计算，**不持久化**也**不在契约上并列暴露**。
- **Prompt 表达**：`DefaultPromptProvider` 的系统提示词显式告知 LLM"输入是 Markdown"，让模型把结构标记当作语义信号利用，而非字面字符。

### AI 实现约定

- 后台流水线 AI 功能按业务能力垂直切片到 `Paperbase.Application/Documents/Pipelines/`，每条流水线一个目录，内含 BackgroundJob（编排入口）+ Workflow（LLM 编排）+ 兜底/辅助组件，全部以 MAF `ChatClientAgent` 形式实现：
  - `Documents/Pipelines/Classification/DocumentClassificationWorkflow` — 分类（同目录还有 `DocumentClassificationBackgroundJob` + `KeywordDocumentClassifier` 兜底）
  - `Documents/Pipelines/Embedding/DocumentEmbeddingWorkflow` — 文本分块 + 向量（同目录还有 `DocumentEmbeddingBackgroundJob` + `TextChunker`）
  - `Documents/Pipelines/RelationInference/DocumentRelationInferenceWorkflow` — 关系推断（目录预留中）
  - `Documents/Pipelines/TextExtraction/DocumentTextExtractionBackgroundJob` — 非 LLM 的文本提取入口
- 文档问答（Chat）走在线请求路径，由 `Paperbase.Application/Chat/DocumentChatAppService`（命名空间 `Dignite.Paperbase.Chat`）承担，检索通过 `Chat/Search/DocumentTextSearchAdapter.CreateSearchFunction(...)` 包装成 MAF `AIFunction` 挂到 `ChatClientAgent`，由模型按 `ChatToolMode.Auto` 自决何时调用；同目录下的 `DocumentRerankWorkflow` 是可选 LLM 精排；模型未调用 search 工具时 `ChatTurnResultDto.IsDegraded = true` 是诚实信号（不做强制注入兜底）；**不保留 FullText 降级**——未向量化文档由上游流水线保证最终被向量化
- 共享 AI 内核（prompt 系统 + `PaperbaseAIBehaviorOptions`）放在 `Paperbase.Application/Ai/`：`IPromptProvider` / `DefaultPromptProvider` / `PromptTemplate` / `PromptBoundary` 同时被后台 Workflow 与 Chat/Search 消费，按"跨消费者就上提"原则升至顶层。AI 配置分两节：`PaperbaseAI`（host 装配 `IChatClient` 用，含凭据）与 `PaperbaseAIBehavior`（绑到 `PaperbaseAIBehaviorOptions`，Application 层行为参数），两节职责正交不可合并。
- 业务模块（如 Contracts）字段提取自实现：注入 `IChatClient`，构造领域专属 `ChatClientAgent`，使用 `RunAsync<T>` 结构化输出反序列化到自己的 POCO
- 业务模块向 Chat 贡献结构化查询工具：实现 `Dignite.Paperbase.Abstractions.Chat.IDocumentChatToolContributor`（绑定 `DocumentTypeCode` + 返回 `IEnumerable<AIFunction>`）并注册为 `ITransientDependency`；`DocumentChatAppService` 在每轮按会话 scope 收集匹配的 contributor，将其工具与内置 `search_paperbase_documents` 一起挂到 `ChatClientAgent`。每个工具实现必须 fail-closed：显式 `IAuthorizationService.CheckAsync(...)` 权限断言 + 显式 `TenantId` 谓词（不依赖 ambient DataFilter）+ 结果集硬上限（`Take(N)`），不得裸跑 raw SQL；反例见 `.claude/rules/doc-chat-anti-patterns.md` 反例 C，参照实现见 `modules/contracts/src/Dignite.Paperbase.Contracts.Application/Chat/ContractChatToolContributor.cs`

## 处理规则

1. 在 core 和 modules 中开发时，严格遵循 `.claude/rules/` 中的规则
2. 开发可复用模块时，**所有公共和受保护方法必须是虚拟（virtual）的**
3. 模块中不要配置中间件，仅在 host 中配置
4. 遵循 ABP 的依赖注入约定，不要手动调用 AddScoped/AddTransient/AddSingleton
5. **改动前先判断是否需要 Issue**：涉及架构决策、影响模块边界、或属于 Slice 任务的改动，**先停下，告知用户开 GitHub Issue 后再动手**；纯实现细节的 fix（如 bug fix、措辞修正）直接用 commit message 记录即可
6. **分析必须果断**：给结论时先抛**判定**再给**理由**，不要列"可能 A / 也许 B / 取决于你"的菜单把判断推回给用户。两条路都可行时，按项目既定偏好（AI-first、不重复造轮、瞄准当下与未来）选一条并说明取舍；只有真正无法靠 `grep` / `Read` 在 30 秒内自查的才允许保留不确定性。禁止 hedging 词："可能"、"也许"、"取决于具体情况"、"两种都可以"——除非确实不知道

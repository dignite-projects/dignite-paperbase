# Paperbase RAG 与向量存储解耦设计报告

## 摘要

Paperbase 当前 QA 流程本质上已经是 RAG：用户问题先被向量化，再从文档 chunk 中检索相关上下文，最后把上下文和问题交给 LLM 生成回答。这个方向是正确的。

但“把文档内容向量化”不应被理解为最终架构。更准确的生产级方向是：

- 使用 embedding / vector search 解决语义召回。
- 使用 keyword / full-text / BM25 解决编号、姓名、日期、金额、条款号等精确召回。
- 使用 rerank 或 agentic retrieval 提升复杂问题的上下文质量。
- 将向量存储实现从 Paperbase 文档业务中解耦。

建议新增核心项目：

```text
Dignite.Paperbase.Rag
```

该项目承载 Paperbase 业务级 RAG 抽象，不命名为 `Dignite.Paperbase.Rag.Abstractions`。它既包含接口，也包含稳定的数据模型、options、检索请求/结果、provider 能力描述和 Agent Framework adapter 契约。

具体实现项目按 provider 拆分：

```text
Dignite.Paperbase.Rag.Pgvector
```

Azure AI Search、Qdrant、Milvus、In-Memory 等 provider 只进入路线图，不在第一阶段创建项目骨架。第一阶段只创建 `Dignite.Paperbase.Rag` 和 `Dignite.Paperbase.Rag.Pgvector`，与当前 OCR provider 的渐进模式保持一致。

## 现状判断

当前代码已经做对的部分：

- LLM 对话通过 `IChatClient` 解耦。
- Embedding 通过 `IEmbeddingGenerator<string, Embedding<float>>` 解耦。
- `DocumentQaWorkflow` 只接收 `QaChunk`，不直接知道向量库。
- `DocumentEmbeddingWorkflow` 输出 `float[]`，工作流本身不依赖 pgvector。
- `IDocumentChunkRepository.SearchByVectorAsync(float[], ...)` 的接口输入没有暴露 pgvector 类型。

当前主要耦合点：

- `DocumentChunk.EmbeddingVector` 直接使用 `Pgvector.Vector`，导致 Domain 层依赖基础设施实现。
- EF Core mapping 中写死 PostgreSQL `vector(N)`。
- `EfCoreDocumentChunkRepository` 直接调用 pgvector 的 `CosineDistance`。
- QA Application 层直接调用 `IDocumentChunkRepository.SearchByVectorWithScoresAsync`，使 chunk repository 同时承担普通仓储和向量检索职责。
- 向量维度由 `PaperbaseDbProperties.EmbeddingVectorDimension` 编译期常量控制，切换 embedding model 时需要 schema 迁移和重建向量。

因此，解耦不是从零开始，而是把已存在的良好边界继续向下推进。

## 推荐边界

不要抽象整个 RAG。Paperbase 应保留自己的业务语义：

- 文档权限。
- 多租户隔离。
- 单文档 QA。
- 跨文档 / 按文档类型 QA。
- chunk 引用格式。
- prompt 策略。
- 低分降级。
- full-text fallback。
- 文档 pipeline 状态。

需要抽象的是“检索索引和向量存储能力”：

```text
Paperbase Application
  -> Dignite.Paperbase.Rag
    -> Provider implementation
      -> Microsoft.Extensions.VectorData or native SDK
```

不建议走：

```text
Paperbase Application
  -> Semantic Kernel VectorStore collection
```

原因是当前方向应参考 Microsoft Agent Framework。Agent Framework 的 C# vector store 集成明确依赖 `Microsoft.Extensions.VectorData.Abstractions`，而不是把 Semantic Kernel Vector Stores 作为应用的主架构边界。Semantic Kernel connector 文档仍可能作为某些 connector 的入口或兼容说明出现，但不应成为 Paperbase 的核心抽象。

## 与 Microsoft Agent Framework 的对齐

Microsoft Agent Framework 支持通过 AI Context Providers 增加 RAG 能力。C# 侧的 `TextSearchProvider` 接收一个搜索函数，将搜索结果注入 `ChatClientAgent` 的上下文。

这与 Paperbase 的推荐边界匹配：

```text
ChatClientAgent
  -> TextSearchProvider
    -> Paperbase search adapter
      -> IDocumentVectorStore.SearchAsync(...)
```

Agent Framework 的 vector store 集成在 C# 侧使用 `Microsoft.Extensions.VectorData.Abstractions`。因此建议采用两层结构：

1. `Dignite.Paperbase.Rag` 定义 Paperbase 业务级接口。
2. provider 内部优先基于 `Microsoft.Extensions.VectorData` 实现；如 provider 能力不足，再使用 native SDK 或 EF Core 查询补齐。

这样既符合 Microsoft Agent Framework 的当前方向，也避免 Paperbase 的 Application 层被某个外部框架的数据模型绑住。

## 不发明轮子的处理原则

本设计的核心原则是复用 Microsoft 已有抽象，不重新设计一套通用 AI / Vector framework。

具体处理如下：

- Chat 和 embedding 继续使用 `Microsoft.Extensions.AI`，不再新增 `IChatProvider`、`IEmbeddingProvider` 之类的 Paperbase 自定义抽象。
- Vector store 的底层 provider 优先使用 `Microsoft.Extensions.VectorData.Abstractions` 及其 connector 生态，不自行维护 Qdrant、Azure AI Search、Postgres、Redis 等数据库 SDK 的统一抽象。
- `Dignite.Paperbase.Rag` 不是 `Microsoft.Extensions.VectorData` 的替代品，而是 Paperbase 的业务适配层。
- `DocumentVectorRecord` 应尽量设计成可以直接映射到 VectorData record 的形态，必要时使用 VectorData 的 key / data / vector 标注。
- `IDocumentVectorStore` 只承载 Paperbase 特有语义，例如 `TenantId`、`DocumentId`、`DocumentTypeCode`、`ChunkIndex`、source citation 字段、score 归一化、按文档删除、QA 降级策略。
- 如果未来 Application 层可以安全、清晰地直接使用 `VectorStoreCollection<TKey, TRecord>`，应优先减少 Paperbase 自定义接口，而不是扩大 `IDocumentVectorStore` 的职责。

换句话说，`Dignite.Paperbase.Rag` 的目标不是“造一个 Paperbase 版 VectorData”，而是把 VectorData 没有也不应该知道的 Paperbase 文档业务语义补上。

## 项目结构

建议目标结构：

```text
core/src/
  Dignite.Paperbase.Rag/
    IDocumentVectorStore.cs
    DocumentVectorRecord.cs
    VectorSearchRequest.cs
    VectorSearchResult.cs
    VectorSearchMode.cs
    VectorStoreCapabilities.cs
    PaperbaseRagOptions.cs
    PaperbaseRagModule.cs

  Dignite.Paperbase.Rag.Pgvector/
    PgvectorDocumentVectorStore.cs
    PgvectorRagOptions.cs
    PgvectorRagModule.cs
```

其他 provider 等有明确需求后再加，避免先搭空骨架造成维护成本。

## 核心接口草案

```csharp
public interface IDocumentVectorStore
{
    VectorStoreCapabilities Capabilities { get; }

    Task UpsertAsync(
        IReadOnlyList<DocumentVectorRecord> records,
        CancellationToken cancellationToken = default);

    Task DeleteByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default);
}
```

这个接口不是通用 vector database abstraction。通用抽象由 `Microsoft.Extensions.VectorData` 承担；该接口只是 Paperbase 面向 QA / RAG 场景的业务门面。provider 内部应优先把它映射到 `VectorStoreCollection<Guid, DocumentVectorRecord>`。

```csharp
public sealed class DocumentVectorRecord
{
    public Guid Id { get; init; }
    public Guid? TenantId { get; init; }
    public Guid DocumentId { get; init; }
    public string? DocumentTypeCode { get; init; }
    public int ChunkIndex { get; init; }
    public string Text { get; init; } = default!;
    public ReadOnlyMemory<float> Vector { get; init; }
    public string? Title { get; init; }
    public int? PageNumber { get; init; }
}
```

```csharp
public sealed class VectorSearchRequest
{
    public Guid? TenantId { get; init; }
    public ReadOnlyMemory<float> QueryVector { get; init; }
    public string? QueryText { get; init; }
    public int TopK { get; init; } = 5;
    public Guid? DocumentId { get; init; }
    public string? DocumentTypeCode { get; init; }
    public double? MinScore { get; init; }
    public VectorSearchMode Mode { get; init; } = VectorSearchMode.Vector;
}
```

```csharp
public sealed class VectorSearchResult
{
    public Guid RecordId { get; init; }
    public Guid DocumentId { get; init; }
    public string? DocumentTypeCode { get; init; }
    public int ChunkIndex { get; init; }
    public string Text { get; init; } = default!;
    public double? Score { get; init; }
    public string? Title { get; init; }
    public int? PageNumber { get; init; }
}
```

```csharp
public enum VectorSearchMode
{
    Vector,
    Keyword,
    Hybrid
}
```

`TenantId` 采用显式契约 + helper 便利层的设计：

- `DocumentVectorRecord.TenantId` 显式保存租户，用于 upsert、重建索引、background job 等场景。
- `VectorSearchRequest.TenantId` 显式指定检索租户，让 provider 不依赖 ambient ABP context，便于测试，也适合 Hangfire、CLI、Function App 等场景。
- `Dignite.Paperbase.Rag` 可提供 `SearchForCurrentTenantAsync(...)` 扩展方法，从 `ICurrentTenant.Id` 填充 request，避免 Application 代码重复手写租户参数。

helper 应放在 `Dignite.Paperbase.Rag` 层，靠近 `IDocumentVectorStore` 接口定义，使 Application、Workflow、未来 Agent Framework adapter 都复用同一便利层。

`DocumentVectorRecord` 和 `VectorSearchResult` 不预留通用 metadata 字典。当前已知需要的 citation / filter 字段应强类型化，例如 `Title`、`PageNumber`；未来若出现真实业务需要，如 `SourceUri`、`FileHash`、`UploadedAt`，应通过显式字段变更加入。这样可以让 provider 映射、索引字段、filter 能力和 API 契约在编译期暴露出来，避免各 provider 私下约定字符串 key。

`Score` 默认契约固定为 `[0, 1]`，越大越相关。provider 应在返回前归一化：

- pgvector cosine：`Score = 1.0 - cosineDistance`。
- BM25 / full-text：provider 内部归一化为 `[0, 1]`。
- Hybrid：provider 内部可使用 RRF 等融合算法，但输出仍必须是 `[0, 1]`。

这样 `MinScore` 在大多数 provider 上可以保持 mode-agnostic。若某个 provider 确实无法或不应该归一化，可将 `Capabilities.NormalizesScore` 设为 `false` 并透传原始分数；此时 Application 层不得直接套用 `QaMinScore`，必须显式处理该 provider 的 score 语义。

## Provider 能力描述

不同 vector store 能力差异很大。建议 provider 暴露能力描述：

```csharp
public sealed class VectorStoreCapabilities
{
    public bool SupportsVectorSearch { get; init; }
    public bool SupportsKeywordSearch { get; init; }
    public bool SupportsHybridSearch { get; init; }
    public bool SupportsStructuredFilter { get; init; }
    public bool SupportsDeleteByDocumentId { get; init; }
    public bool NormalizesScore { get; init; }
}
```

`IDocumentVectorStore.Capabilities` 应作为只读属性暴露。能力通常在 provider 启动时确定，不随单次调用变化。

这样 Application 层可以做清晰降级：

- provider 不支持 hybrid 时退回 vector。
- provider 不支持 structured filter 时启动失败，避免跨租户或跨文档泄漏。
- provider 不提供归一化 score 时，不能直接套用 `QaMinScore`。

## 解耦层级

建议采用分层实施，而不是一次性切到新架构。

### Level 0：净化 Domain 的 Pgvector 依赖

把 `DocumentChunk.EmbeddingVector` 从 `Pgvector.Vector` 改为 `float[]` 或 `ReadOnlyMemory<float>`，由 EF Core provider mapping 负责转换成 pgvector。

价值：

- 修复 Domain 层依赖基础设施类型的问题。
- 保持底层仍是 PostgreSQL pgvector。
- 风险低，编译期能暴露大部分问题。

限制：

- 仍不能切换外部 vector database。
- 仍然是 EF Core + pgvector 查询。

### Level 1：继续用仓储接口扩展 provider

理论上可以让不同 provider 实现 `IDocumentChunkRepository`。但不推荐作为主路线。

原因：

- `IRepository<DocumentChunk, Guid>` 语义偏关系数据库 CRUD。
- 外部 vector database 不一定适合实现 ABP repository。
- 关系数据和向量索引的事务边界会变得别扭。

### Level 2：Paperbase 自定义 RAG 端口

新增 `Dignite.Paperbase.Rag`，定义 `IDocumentVectorStore`。

价值：

- Application 层脱离 `IDocumentChunkRepository` 的向量检索方法。
- 与项目已有 OCR provider 风格相近，可使用 `UseProvider<T>()` 或模块依赖方式装配。
- Paperbase 可保留自己的多租户、文档类型、引用和降级语义。

这一层落地后，`IDocumentChunkRepository` 应回归 chunk 元数据 CRUD。`SearchByVectorAsync` / `SearchByVectorWithScoresAsync` 应从 repository 接口移除，向量检索唯一入口变为 `IDocumentVectorStore.SearchAsync`。Pgvector provider 内部仍可使用 EF Core 查询实现，但不要把向量检索方法继续暴露在普通 repository 上。

### Level 3：Provider 内部使用 Microsoft.Extensions.VectorData

在 provider 内部用 `Microsoft.Extensions.VectorData.Abstractions` 对接具体向量库。

价值：

- 与 `Microsoft.Extensions.AI` 同生态。
- 与 Microsoft Agent Framework 当前 C# 方向一致。
- 未来切换 pgvector、Azure AI Search、Qdrant、Redis、Cosmos DB、Elasticsearch 等实现时，provider 内部改动更小。

注意：

- VectorData 的 filter 不会自动应用 ABP 全局过滤器，provider 必须使用 `VectorSearchRequest.TenantId` 显式添加租户 filter。
- 不同 connector 对 hybrid search、filter、score 的支持不完全一致。
- provider 仍要负责把 Paperbase 的 request / result 映射到 VectorData record 和 filter。

不同 provider 的租户隔离路径：

| Provider | 多租户机制 |
|---|---|
| Pgvector / EF Core | ABP `IMultiTenant` 全局过滤器可自动生效；provider 仍应接受 request 中的 `TenantId` 作为显式约束 |
| Qdrant / Milvus | Provider 根据 `VectorSearchRequest.TenantId` 拼接 filter |
| Azure AI Search | Index filter 表达式注入 `TenantId eq '...'` |
| In-Memory | Provider 内部 LINQ filter |

## 混合检索是正交优化

向量库解耦解决的是“可替换性”。但 QA 质量提升通常更依赖“召回质量”。因此 hybrid search 应作为独立优化方向推进。

Paperbase 的文档场景很适合混合检索：

- 合同号、发票号、证照号、姓名、日期、金额：keyword / full-text 更强。
- 条款含义、事实相似、自然语言问题：vector search 更强。
- 多条件问题：hybrid + rerank 更稳。

推荐演进：

1. Pgvector 向量召回。
2. PostgreSQL full-text / trigram 关键词召回。
3. RRF 融合两路候选。
4. 复用现有 LLM rerank 或后续接入专用 reranker。
5. 最终将 hybrid search 能力沉入 `IDocumentVectorStore.SearchAsync` 的 `VectorSearchMode.Hybrid`。

## Agentic RAG 演进

在基础检索稳定后，可引入 Agent Framework 的 RAG 模式：

- 用 `TextSearchProvider` 包装 Paperbase search adapter。
- 对复杂问题使用 multi-query 或 agentic retrieval。
- 让 agent 根据问题选择搜索工具或搜索范围。
- 保留 Paperbase 自己的权限和多租户过滤。

建议不要一开始就替换 `DocumentQaWorkflow`。先把 `IDocumentVectorStore` 做稳，再添加 Agent Framework adapter 作为可选路径。

## 配置建议

核心配置：

```json
{
  "PaperbaseRag": {
    "Provider": "Pgvector",
    "EmbeddingDimension": 1536,
    "DefaultTopK": 5,
    "MinScore": 0.65,
    "DefaultSearchMode": "Vector"
  }
}
```

`EmbeddingDimension` 建议归入 `PaperbaseRagOptions`。`PaperbaseAIOptions` 保留 chunk、QA、rerank 等编排选项；RAG index 相关的维度、默认检索模式、默认阈值归 `PaperbaseRagOptions` 管理，并继续通过启动期 validation 保证配置与 schema 一致。

Pgvector provider 配置：

```json
{
  "PaperbaseRag:Pgvector": {
    "DistanceMetric": "Cosine",
    "UseHnswIndex": true
  }
}
```

Azure AI Search provider 配置：

```json
{
  "PaperbaseRag:AzureAISearch": {
    "Endpoint": "https://example.search.windows.net",
    "IndexName": "paperbase-documents"
  }
}
```

## 文档与 Issue 边界

本报告保留架构决策、权衡、接口草案、风险约束和 Slice 拆分依据。进入实施前，应将下列 Slice 拆成 GitHub Issues，并用 Issue 列表跟踪进度；docs 不作为路线图看板频繁更新。

采用的边界是：docs 保留 Slice 的“做什么 / 范围 / 验收”，Issues 承载实施讨论、PR 关联、状态流转和验收记录。后续可在每个 Slice 小节末尾补充对应 Issue 链接，但不在 docs 中维护进度状态。

## 推荐实施路线

### Slice 1：报告确认与 Issue 拆分

目标：确认本报告作为统一设计口径，并将后续 Slice 拆成独立 GitHub Issue。

验收：

- Issue 明确范围、验收条件和依赖顺序。
- 每个 Issue 只覆盖一个可独立 review 的变更。
- docs 保留架构背景，进度跟踪进入 Issues。

### Slice 2：Phase 0 Domain 净化

目标：Domain 不再依赖 `Pgvector.Vector`。这一步不引入任何新抽象，只修复分层越界。

范围：

- `DocumentChunk`
- EF Core mapping
- pgvector conversion
- 相关测试

验收：

- Domain project 不再引用 `Pgvector`。
- 现有 pgvector 查询行为不变。
- 编译和相关测试通过。

### Slice 3：新增 `Dignite.Paperbase.Rag`

目标：建立 Paperbase RAG 业务级抽象。

范围：

- 新增 `IDocumentVectorStore`。
- 新增 request / result / record / options。
- 新增 module。
- 提供 `SearchForCurrentTenantAsync(...)` helper。
- 不改变现有 QA 行为。

验收：

- 新项目可编译。
- 无 provider 依赖。
- public / protected 方法遵守模块可扩展规则。

### Slice 4：Pgvector provider adapter

目标：让现有 pgvector 实现成为 `IDocumentVectorStore` 的默认 provider。

范围：

- 新增 `Dignite.Paperbase.Rag.Pgvector`。
- 内部复用现有 `IDocumentChunkRepository` 或 EF Core 查询。
- 将 cosine distance 映射为统一 score。

验收：

- 单文档 QA 行为不变。
- 跨文档 QA 行为不变。
- `QaMinScore` 仍可用。

### Slice 5：Application 层切换到 RAG 抽象

目标：`DocumentQaAppService` 不再直接依赖向量检索仓储。

范围：

- `DocumentQaAppService`
- embedding query 生成逻辑
- QA tests

验收：

- Application 层只依赖 `Dignite.Paperbase.Rag`。
- 可用 fake `IDocumentVectorStore` 测试 QA 编排。
- provider 可替换性可通过测试证明。
- `IDocumentChunkRepository` 移除向量搜索方法。
- `EmbeddingDimension` 配置归入 `PaperbaseRagOptions`。

### Slice 6：写入路径切换到 RAG 抽象

目标：embedding pipeline 通过 `IDocumentVectorStore.UpsertAsync` 写入索引。

范围：

- `DocumentEmbeddingWorkflow` 或 background job。
- 删除文档 / 重建 embedding 的清理逻辑。

验收：

- 文档重建 embedding 后索引一致。
- 删除文档后向量记录清理。
- 多租户数据隔离测试通过。

### Slice 7：Hybrid search

目标：提升召回质量。

范围：

- `VectorSearchMode.Hybrid`。
- Pgvector + PostgreSQL full-text / trigram 或 provider 原生 hybrid。
- RRF 融合。
- rerank 接入点。

验收：

- 精确编号类问题召回提升。
- 语义类问题不回退。
- score / source citation 字段可解释。

### Slice 8：Agent Framework adapter

目标：为 Agent Framework RAG 做可选接入。

范围：

- `TextSearchProvider` adapter。
- source name / source link / citation 字段。
- 可选 agentic retrieval 实验。

验收：

- 不替换现有 QA workflow。
- 同一套 `IDocumentVectorStore` 可服务传统 QA 和 Agent Framework RAG。

## Code Review 基线

后续实现完成后，review 以本节作为最低验收基线。若实现与本节冲突，应优先要求修改代码；只有发现本设计本身有明确缺陷时，才回到设计讨论。

### 架构拒收项

- Domain 层仍引用 `Pgvector`、EF Core、VectorData connector 或任何 provider SDK。
- `Dignite.Paperbase.Rag` 变成通用 vector database abstraction，或重新发明 `Microsoft.Extensions.VectorData` 已有能力。
- `Dignite.Paperbase.Rag` 引用 Pgvector、Qdrant、Azure AI Search、EF Core provider 等具体实现包。
- `DocumentVectorRecord` 或 `VectorSearchResult` 重新加入通用 `Metadata` / `ExtraProperties` 字典。
- `VectorSearchRequest` 没有显式 `TenantId`，或 provider 查询未使用租户过滤。
- Application 层继续通过 `IDocumentChunkRepository.SearchByVector*` 做向量检索。
- provider 返回的 `Score` 语义不清，或在 `NormalizesScore = true` 时没有保证 `[0, 1]` 且越大越相关。
- reusable module 中配置 middleware，或手动 `AddScoped` / `AddTransient` / `AddSingleton` 绕过 ABP DI 约定。

### 必查清单

- `DocumentChunk.EmbeddingVector` 已从 `Pgvector.Vector` 净化为 provider-neutral 类型，并保留维度校验。
- `IDocumentVectorStore` 只包含 `Capabilities`、`UpsertAsync`、`DeleteByDocumentIdAsync`、`SearchAsync` 等业务门面能力。
- `VectorStoreCapabilities` 被 Pgvector provider 正确声明，尤其是 `SupportsStructuredFilter`、`SupportsDeleteByDocumentId`、`NormalizesScore`。
- `SearchForCurrentTenantAsync(...)` helper 位于 `Dignite.Paperbase.Rag` 层，并只负责填充 `VectorSearchRequest.TenantId` 后委托 `SearchAsync`。
- `PaperbaseRagOptions.EmbeddingDimension` 与 schema / startup validation 一致；`PaperbaseAIOptions` 不再承担索引维度配置。
- Pgvector provider 内部可以复用 EF Core 查询，但普通 repository 接口不再暴露向量检索入口。
- `DocumentQaAppService` 只依赖 `Dignite.Paperbase.Rag` 的抽象进行检索编排，不直接依赖 provider。
- 写入路径通过 `IDocumentVectorStore.UpsertAsync`，删除 / 重建路径通过 `DeleteByDocumentIdAsync` 或等价 provider 操作保持索引一致。
- public / protected methods 遵守 module 可扩展要求，必要的 helper 使用 `virtual` 或扩展方法。

### 测试要求

- Domain / build 测试能证明 Domain project 不再依赖 `Pgvector`。
- Application 测试使用 fake `IDocumentVectorStore` 覆盖单文档 QA、跨文档 QA、低分无结果、full-text fallback。
- Pgvector provider 集成测试覆盖租户过滤、`DocumentId` 过滤、`DocumentTypeCode` 过滤、score 归一化、按文档删除。
- embedding pipeline 测试覆盖 upsert、重建、删除后索引清理。
- 若实现 hybrid search，测试必须覆盖 vector-only fallback、RRF 融合排序、精确编号类 query 的召回。

## 风险与约束

- 多租户过滤必须由 provider 使用 `VectorSearchRequest.TenantId` 显式处理，不能依赖外部数据库自动处理。
- `Score` 默认对 Application 层固定为 `[0, 1]` 且越大越相关；`Capabilities.NormalizesScore = false` 是显式逃逸口。
- Hybrid search 支持度不一致，需要 capabilities 和降级策略。
- Embedding dimension 变化仍需要重建索引。
- 外部 vector database 与关系数据库不是同一事务，需要设计幂等 upsert 和重建任务。
- Citation 字段必须在 upsert 时写入，否则后续很难补齐引用质量。
- Chat memory vector store 与 document knowledge vector store 应分 collection / index，避免语义和权限混淆。

## 结论

综合两份方案后，推荐路线是：

1. 承认当前 QA 是 RAG，但不要把 RAG 简化为纯向量。
2. 核心抽象项目命名为 `Dignite.Paperbase.Rag`。
3. Paperbase 自己定义业务级 `IDocumentVectorStore`，Application 层只依赖它。
4. Provider 内部优先使用 `Microsoft.Extensions.VectorData.Abstractions`，与 Microsoft Agent Framework 当前方向对齐。
5. 不以 Semantic Kernel Vector Stores 作为主架构边界。
6. 实施上先做 Phase 0 / Level 0 Domain 净化，再做 RAG 抽象和 Pgvector adapter。
7. 第一阶段只创建 `Dignite.Paperbase.Rag` 和 `Dignite.Paperbase.Rag.Pgvector`。
8. `TenantId` 在 record 和 request 中显式传递；Rag 层提供 `SearchForCurrentTenantAsync(...)` helper 兼顾 ABP 业务代码便利。
9. 不预留通用 metadata 字典；当前需要的 citation / filter 信息使用强类型字段，未来真实需要再显式扩展契约。
10. Hybrid search 与 Agent Framework adapter 作为后续独立 Slice 推进。

这条路线既保留现有 pgvector 投资，又为 Azure AI Search、Qdrant、其他 VectorData provider、混合检索和 Agent Framework RAG 留出空间。

## 参考

- Microsoft Agent Framework RAG: https://learn.microsoft.com/agent-framework/agents/rag
- Microsoft Agent Framework Vector Stores: https://learn.microsoft.com/agent-framework/integrations/#vector-stores
- .NET RAG concept: https://learn.microsoft.com/dotnet/ai/conceptual/rag
- Vector databases for .NET AI apps: https://learn.microsoft.com/dotnet/ai/vector-stores/overview

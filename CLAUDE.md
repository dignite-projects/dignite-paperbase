# Dignite Paperbase

这是一个 ABP 框架的项目，遵循 `.claude/rules/` 中的 ABP 核心约定和模块模板。

## 项目组织

项目分为四个主要目录：
- **core/** - ABP 应用程序核心，遵循 abp-core.md 规则
- **modules/** - 可复用业务模块，每个模块遵循 module-template.md 的结构和虚拟方法要求
- **host/** - 单租户测试主机，仅在此配置中间件（OnApplicationInitialization）
- **docs/** - 开发者文档（`configuration.md` 等操作文档）和内部设计文档（`design/` 子目录）

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
   - **核心 AI 能力（分类 / 向量-RAG / 关系推断 / 问答）直接在 Application 层落地**——通过 Microsoft Agent Framework (MAF) 1.0 的 `ChatClientAgent` 实现，不再独立成 AI 模块
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

### AI 实现约定

- 核心三大 AI 功能 + QA 全部以 MAF `ChatClientAgent` 形式落地在 `Paperbase.Application/Documents/AI/Workflows/`：
  - `DocumentClassificationWorkflow` — 分类
  - `DocumentEmbeddingWorkflow` — 文本分块 + 向量
  - `DocumentRelationInferenceWorkflow` — 关系推断
  - `DocumentQaWorkflow` — RAG / FullText 问答
- BackgroundJob 在 `Paperbase.Application/Documents/BackgroundJobs/`，调用上述 Workflow 编排流水线
- 业务模块（如 Contracts）字段提取自实现：注入 `IChatClient`，构造领域专属 `ChatClientAgent`，使用 `RunAsync<T>` 结构化输出反序列化到自己的 POCO

## 处理规则

1. 在 core 和 modules 中开发时，严格遵循 `.claude/rules/` 中的规则
2. 开发可复用模块时，**所有公共和受保护方法必须是虚拟（virtual）的**
3. 模块中不要配置中间件，仅在 host 中配置
4. 遵循 ABP 的依赖注入约定，不要手动调用 AddScoped/AddTransient/AddSingleton
5. **改动前先判断是否需要 Issue**：涉及架构决策、影响模块边界、或属于 Slice 任务的改动，在动手实现前提示用户先开 GitHub Issue；纯实现细节的 fix（如 bug fix、措辞修正）直接用 commit message 记录即可

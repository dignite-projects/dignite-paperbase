# Dignite Paperbase

> **定位（一句话）**：Paperbase = 物理纸质文档 → 可信数字化数据的**通道层**。
> **不消费、不占有、不深入业务**——出口给下游 RAG 平台 / 业务系统 / AI 客户端等消费方。

这是一个 ABP 框架的项目，遵循 `.claude/rules/` 中的 ABP 核心约定。

## 数据流

```
物理纸张 / 扫描件 / 照片 / PDF 影像 / Office 文件
    ↓
[Paperbase 通道]：OCR + Markdown + 通用元数据 + 可选自定义字段抽取
    ↓ （REST / EventBus / MCP server / Webhook）
    ├─→ 下游 RAG 平台（做 RAG 问答）
    ├─→ 财务系统 / CLM / HR / ERP 等业务系统
    ├─→ Claude Desktop / Cursor / 任意 MCP 客户端
    └─→ 任何消费方（按需自建 consumer）
```

## 项目组织

- **core/** - Paperbase 通道实现（ABP 应用程序栈），遵循 `.claude/rules/abp-core.md`
- **host/** - 宿主应用：配置 OCR / Markdown / LLM provider；唯一可配置中间件的位置
- **docs/** - 面向运维 / 配置 / API 的文档；设计决策走 GitHub Issues，不在 docs/ 下落地
- **modules/** - **不在 Paperbase 范畴**。下游业务消费方（合同管理 / 发票管理等）在自己的仓库实现，通过订阅 EventBus / 调用 MCP server / REST 接入。该目录下尚存的 `contracts/` 是迁移期遗留，按 [#165](https://github.com/dignite-projects/dignite-paperbase/issues/165) 计划剥离为下游参考实现

## 架构设计

Paperbase 采用**两层架构**（业务层不在 Paperbase 范畴）：

### 第一层：Core（通道实现）

`core/` 包含通道的全部能力：

1. **Dignite.Paperbase.Abstractions（扩展契约层）**
   - **位置**：依赖拓扑的最底层（无其他 Paperbase 项目依赖）
   - **职责**：提供 Host 扩展和下游消费方接入所必需的契约
   - **内容**：
     - 文档类型注册：`DocumentTypeDefinition`、`DocumentTypeOptions`
     - 多阶段集成事件 ETO：`DocumentUploadedEto` / `OCRCompletedEto` / `DocumentClassifiedEto` / `MetadataExtractedEto` / `CustomFieldsExtractedEto` / `DocumentReadyEto`
     - 文本提取契约：`ITextExtractor`、`TextExtractionContext`、`TextExtractionResult`
   - **约束**：不依赖任何其他 Paperbase 项目，仅依赖 ABP 基础模块

2. **Dignite.Paperbase 核心模块栈**（标准 ABP 分层：`Domain.Shared` / `Domain` / `Application` / `EntityFrameworkCore` / `HttpApi`）
   - **通道核心能力都在 Application 层**：文档存储、OCR pipeline 编排、文档分类（LLM 自动分类至 Host / 租户已定义类型）、系统通用字段抽取、类型绑定字段抽取（B 机制：Host 字段 + 租户字段）、出口事件发布、MCP server 实现

3. **文本提取能力栈（三层契约 + 多 Provider）**——核心可插拔点
   - **`Dignite.Paperbase.TextExtraction`** —— orchestrator + 默认 `ITextExtractor` 实现（`DefaultTextExtractor`：按文件扩展名 dispatch，图片走 OCR；其他走 Markdown Provider，PDF 无文本层时 fallback OCR）。同项目内声明 `IMarkdownTextProvider` 副契约
   - **`Dignite.Paperbase.Ocr`** —— OCR Provider 实现侧的最小契约层（`IOcrProvider` / `OcrOptions` / `OcrResult`，Markdown-first 强约束）。第三方 OCR 接入只引用此项目，看不到 orchestrator 或 `IMarkdownTextProvider` 副契约
   - **OCR Provider 实现**：`Dignite.Paperbase.Ocr.PaddleOcr`（Host 当前默认，本地 sidecar，PP-StructureV3 走 CPU 即可，输出 Markdown）与 `Dignite.Paperbase.Ocr.AzureDocumentIntelligence`（云方案，高精度）；Host 二选一启用，切换时 `[DependsOn]` + `.csproj ProjectReference` 两处同步
   - **Markdown Provider 实现**：`Dignite.Paperbase.TextExtraction.ElBrunoMarkItDown`（基于 ElBruno.MarkItDotNet，覆盖 PDF/Word/HTML/纯文本/CSV/RTF/EPUB 等数字版文档）
   - **与 OCR Provider 的不对称是故意的**——Markdown Provider 与 orchestrator 耦合度高，契约 + 实现都靠近 TextExtraction；OCR Provider 第三方实现概率更高（云服务 / 本地 sidecar），独立薄契约层给它稳定边界

### 第二层：Modules（下游消费方 / 参考实现，不在 Paperbase 范畴）

**业务模块（合同管理 / 发票管理 / HR 档案管理等）不在 Paperbase 范畴。** 下游消费方在自己的仓库 / 部署中实现，通过 Paperbase 出口契约接入：

| 接入方式 | 适合场景 |
|---------|---------|
| **EventBus 订阅** | 业务系统响应 Paperbase 多阶段事件（如 `DocumentReadyEto`），在自己的聚合根里持久化业务记录 |
| **MCP server 调用** | AI 客户端（Claude Desktop / Cursor / 自建 agent）通过 MCP 协议读取 Paperbase 文档资源、订阅 lifecycle 通知 |
| **REST API** | 通用程序化访问 |
| **Webhook** | 传统系统的事件回调消费方式 |

**典型参考实现模式**（下游业务消费方）：

1. 订阅 `DocumentClassifiedEto` 或 `DocumentReadyEto`
2. 用租户字段抽取（B 机制）拿到结构化业务字段
3. 在自己的聚合根（如下游 `Contract` / `Invoice`）持久化 + 提供业务 API / UI
4. 业务记录是 Paperbase Document 的"派生投影"——`Document` 仍是 truth source

**Paperbase 不亲自做下游的事**——不预置业务 schema（合同金额 / 发票号 / 税额等）、不写业务系统专属连接器、不实现业务工作流（审批 / 续签）。

> `modules/contracts/` 目录在迁移期内仍可能存在，按 [#165](https://github.com/dignite-projects/dignite-paperbase/issues/165) 剥离为独立参考实现项目。改动 Paperbase 时，**不要**以现存 `modules/contracts/` 的耦合方式作为未来扩展的范本

### 第三层：Host（宿主应用）

`host/` 仅作为容器：

- 在 `[DependsOn(...)]` 中声明依赖的核心模块和 provider 实现
- 在 `ConfigureServices()` 中：
  - 配置 OCR Provider（默认 PaddleOCR，可切 Azure Document Intelligence）+ Markdown Provider（ElBruno MarkItDown）
  - 注册 `IChatClient` + `IEmbeddingGenerator<string, Embedding<float>>`（如 Azure OpenAI / Anthropic / Ollama 等，按 `Microsoft.Extensions.AI` 生态选择）—— 供内置 LLM 分类、类型绑定字段抽取（B 机制）使用
  - 配置出口事件订阅、Webhook 端点、MCP server 端点
- **仅在此处配置中间件**（`OnApplicationInitialization`）
- 不实现任何业务逻辑

> **关键约束**：**LLM / OCR provider 与 API key 配置在 host 部署层，不开放给终端客户**。这是与"客户在管理后台配置 LLM"路径的核心区别。客户是业务用户不是技术用户，让客户填 API key 是产品哲学错误

### 依赖流向

```
Host Application
    ├── 注册 IChatClient + IEmbeddingGenerator
    └── DependsOn:
        ├── Dignite.Paperbase.Application（通道核心 + 编排 + LLM 分类 / 字段抽取）
        ├── Dignite.Paperbase.TextExtraction（orchestrator + IMarkdownTextProvider 契约）
        ├── Dignite.Paperbase.TextExtraction.ElBrunoMarkItDown（Markdown Provider 实现）
        └── Dignite.Paperbase.Ocr.PaddleOcr（OCR Provider，当前默认；可切换 Ocr.AzureDocumentIntelligence）

Dignite.Paperbase.Abstractions（扩展契约层，无其他 Paperbase 项目依赖）
    ├── DocumentTypeDefinition / DocumentTypeOptions
    ├── 多阶段集成事件 ETO（DocumentUploaded / OCRCompleted / DocumentClassified / MetadataExtracted / CustomFieldsExtracted / DocumentReady）
    └── ITextExtractor + TextExtractionContext / TextExtractionResult

Dignite.Paperbase.Ocr（OCR Provider 最小契约层，无其他 Paperbase 项目依赖）
    ├── IOcrProvider + OcrOptions + OcrResult（Markdown-first）
    └── 被 Ocr.PaddleOcr / Ocr.AzureDocumentIntelligence 等 Provider 实现项目引用
```

**核心约束**：

- **单向依赖**：Abstractions 处于最底层，被所有上层引用
- **编排在 Application**：BackgroundJob、Workflow、PipelineRun 生命周期、Document 读写均在 Paperbase.Application
- **业务模块不在 Paperbase**：下游业务消费方通过 EventBus / MCP / REST 解耦消费，**不允许**在 Paperbase Core 内部新增业务模块依赖

## 两层文档类型体系

**文档类型（document type）是类型绑定字段的容器**——所有类型绑定字段必须挂在某个文档类型下。

| 层级 | 谁定义 | 范围 | 例子 / 说明 |
|------|-------|------|------------|
| **Host 部署类型** | Host 部署者 | 该部署所有租户共享 | 例：合同 / 发票 / 收据 / 报告 / 简历 / 证照 / 医疗病历 / 学籍档案 / 政策文件 / 通用文档（fallback） |
| **租户级类型** | 租户（per-tenant）| 该租户私有 | 例：某律所私有的"案件卷宗"类型，某医院私有的"会诊记录"类型 |

**Paperbase 不内置任何文档类型**——所有类型由部署方或租户按业务实际需要定义，避免内置类型与垂直业务脱节。Host 部署时**应至少定义一个 fallback 通用类型**，承接无法归类的文档。

**分类执行机制**：

- 上传时 Paperbase 自动用 LLM 跑分类 prompt → 在该租户可见的类型集合（Host 类型 ∪ 该租户类型）内归类
- 置信度低或操作员不同意 → 操作员 UI 可手动修正
- 修正后重新触发后续 pipeline（如对应类型的字段抽取）

## 字段架构

字段分两类组织：**系统通用字段**（Paperbase pipeline 自动计算，与文档类型无关）+ **类型绑定字段**（按 schema 抽取，必须挂在某个类型下）。

### 系统通用字段（Paperbase pipeline 计算，无需配置）

由 Paperbase 处理管道自动产生，适用于所有文档，**不**需要任何 schema 配置：

filename / size / format / page count / language / OCR confidence / document date / title / summary / topic tags / 通用 NER 实体 / classification（指文档类型归类结果）。

### 类型绑定字段（B 机制）

类型绑定字段必须挂在某个文档类型下，按谁定义分两层：

| 层级 | 谁定义 | 可绑定的类型 | 例子 |
|------|-------|------------|------|
| **Host 字段** | Host 部署者 | 挂在 Host 类型下 | 例：在"医疗病历"type 下加"科室"字段 |
| **租户字段** | 租户（per-tenant）| 挂在 Host 类型 OR 该租户类型下 | 例：租户在 Host "合同" type 下加"甲方/乙方/合同金额"；或在自定义"案件卷宗" type 下加"当事人/案由"字段 |

**(B) 机制本质**：Paperbase 提供"按 schema 抽取"的通用引擎——Host 或租户配 schema，引擎按 schema 抽取。Paperbase Core **不预置任何业务字段定义**（合同金额 / 发票号 / 税额等不写死）。这是机制与策略分离。

## 出口契约

Paperbase 通过四种出口给下游消费方：

| 出口 | 协议 | 主要受众 |
|------|------|---------|
| **REST API** | HTTP | 通用程序化访问 |
| **MCP server** | MCP（含 `notifications/resources/updated`）| Claude Desktop / Cursor / 任意 MCP 客户端 |
| **EventBus** | ABP DistributedEventBus | 业务系统 / 自建 consumer |
| **Webhook** | HTTP POST | 传统系统消费方式 |

### 出口事件契约（多阶段 + 薄载荷）

| 阶段事件 | 触发时机 | 受置信度门槛约束 |
|---------|---------|----------------|
| `DocumentUploadedEto` | 文档上传完成 | 否 |
| `OCRCompletedEto` | OCR 完成（含 confidence 指标） | 否 |
| `DocumentClassifiedEto` | 文档分类完成 | 否 |
| `MetadataExtractedEto` | 系统通用字段 + Host 字段抽取完成 | 否 |
| `CustomFieldsExtractedEto` | 租户字段（B 机制）抽取完成 | 否 |
| `DocumentReadyEto` | **全流水线完成 + 通过置信度门槛** | **是** |

**载荷设计**：事件载荷一律薄（ID + 关键元数据），下游通过 REST/MCP 回拉详细数据。

**OCR 置信度门槛**：

- **设计意图**：低质量 OCR 不应自动污染下游
- **门槛执行点**：**仅 `DocumentReadyEto` 受约束**——早期阶段事件正常发，但下游主要消费方默认订阅 `DocumentReadyEto`
- **门槛配置**：host 部署级（默认值）+ per-tenant 可覆盖
- **不达标的文档**：仍然存（不丢失，不删除）；早期阶段事件正常发布；`DocumentReadyEto` 暂不发；文档进入操作员 UI 的"待人工审核队列"；操作员修正 / 手动确认通过 → 触发 `DocumentReadyEto`

**事件去重与替换**：

- **设计意图**：避免重复消费 + 同一份数据迭代更新时不污染下游
- **去重 key**：`(TenantId, DocumentId, EventType)`
- **替换语义**：
  - 同一 key 的事件**未被消费**（in-flight）→ 新事件**替换**旧事件
  - 同一 key 的事件**已被消费** → 发新事件（视作 update）
- **状态追踪**：Paperbase 维护事件状态表（in-flight / consumed）

## OUT of scope（明确不做）

通道定位下，以下能力**不在** Paperbase 范畴。改动若试图触碰这些边界，先停下开 Issue 讨论：

**RAG 应用层**：

- ❌ 向量化（embedding model 选择是下游 RAG 的事）
- ❌ 向量存储（vector DB 是下游 RAG 基础设施）
- ❌ 检索引擎
- ❌ Chat / RAG 问答 / NL search
- ❌ Agent / Workflow 编排（不做 Agent Canvas 类似物）
- ❌ MCP **client**（只做 MCP server，不调外部 MCP 工具）
- ❌ 标准化 chunking（chunking 策略让下游 RAG 决定）

**业务层**：

- ❌ 业务字段抽取的预置 schema（合同金额 / 发票号 / 税额等不预置；客户用 (B) 机制自配）
- ❌ 行业 vertical 导入模板的预置（各类 ERP / 财务 / HR 系统等）——租户可用"租户字段（B 机制）+ 自定义导出模板配置"组合出，但 Paperbase 不沉淀
- ❌ 业务工作流（审批 / 状态机 / 续签）
- ❌ 业务系统专属连接器
- ❌ 业务模块（合同管理 / 发票管理 / HR 档案管理等）——由下游消费方在自己仓库实现

**配置层**：

- ❌ 让终端客户配置 LLM provider / API key（host 部署层配好）

## Markdown-first 数据流（强制）

Paperbase 是通道，Markdown 是出口的**唯一文本载荷**。遇到取舍时优先保持 Markdown-first：

- **OCR / 数字版抽取**：`ITextExtractor` / `IMarkdownTextProvider` / `IOcrProvider` 实现方**必须**输出 Markdown，**不得**退回 plain text 路径
  - **对结构化文档而言**（合同 / 政策 / 报告 / CSV / 有标题的 DOCX / PP-StructureV3 / Azure DI prebuilt-document）——标题、表格、列表是下游切块和 LLM 理解的**真信号**，全力利用
  - **对无结构内容而言**（OCR 散段落 / 纯 txt / PP-OCRv4 行级输出 / 单句便签）——Markdown 是**容器命名**，**不是**信号增益；保留 Markdown 路径只是为了下游 chunker / 内置 LLM 分类 / 自定义字段抽取消费同一种格式。诚实承认这一点，不要把扁平段落包装成"也是 Markdown 信号"
  - **翻译职责在 Provider 内部完成**——`OcrResult` / `TextExtractionResult` 不暴露 RawText 字段，Provider 拿到底层服务的纯文本输出后**自己**负责包成扁平 Markdown（例如 `string.Join("\n\n", paragraphs)`），不允许把 plain-text-to-markdown 的兜底逻辑泄漏给上游 orchestrator
- **持久化**：`Document.Markdown` 是 Document 聚合根上唯一的文本字段，**禁止**在 `Document` 或事件载荷上引入并行的 plain-text 字段
- **下游消费**：下游 RAG 向量化 / 内置 LLM 分类 / 类型绑定字段抽取，统一消费 Markdown
- **纯文本投影**：仅在消费侧（如关键字兜底分类器）按需通过 `Dignite.Paperbase.Documents.MarkdownStripper.Strip(...)` 即时计算，**不持久化**也**不在契约上并列暴露**
- **Prompt 表达**：内部 LLM 系统提示词显式告知"输入是 Markdown"，让模型把结构标记当作语义信号利用

**Markdown-first 是工程默认，不是哲学原则。** Markdown 是文本载荷，但 **out-of-band 信号**（坐标 / 置信度 / page metadata / 表单 key-value 结构 / 印章位置）与 Markdown **正交**。未来若需 page-aware citations、签章定位、表单 key-value 抽取，应作为 `TextExtractionResult` 上**具名可选独立扩展字段**（例如 `IReadOnlyList<PageBlock>? PageBlocks`，可空、与 Markdown 不耦合），或独立 extractor 接口（与 `ITextExtractor` 正交）——不被"Markdown 是唯一文本载荷"的字面理解挡掉。

- **禁用模式**：在 `TextExtractionResult` 上加 `Dictionary<string, object>` / `Dictionary<string, string>` 类型的**通用"扩展槽"**——这是 code smell，未来类型不清、消费侧 cast 满天飞、对 LLM-facing schema 不友好
- **正确做法**：每加一种 out-of-band 信号**单独开 Issue 讨论**（属架构决策），按需加**具名、强类型、可空**的字段；如果该信号与 OCR 强相关而与 Markdown Provider 无关，考虑加在 `OcrResult` 而非 `TextExtractionResult` 上以避免责任错位

**Document 字段扩展判定**：上述原则在 transient transport（`TextExtractionResult` / `OcrResult`）层级，到 `Document` 聚合根（持久化层、跨下游消费方共享的 truth source）规则更严。两轴判定：

1. **文本类型字段：永远只有 `Markdown` 一个。** 这是 Markdown-first 在持久化层的硬约束（已被 `Document.SetMarkdown` 的 immutability 强保护在代码层面执行）。任何派生文本（Summary / Outline / SectionsJson）走 `MarkdownStripper.Strip` 或切块器在消费侧投影，**不持久化**。`Title` 是 Markdown 派生的展示快照（不可变），不是新文本载荷；`ClassificationReason` 是 AI 决策解释（不是文档内容）
2. **非文本类型字段：按"通用 truth source vs 业务专属"判定**：
   - **跨下游消费方共享的通用 truth source**（如 `PageBlocks` 用于任何业务的 citation 高亮、OCR Provider name/version 用于调试）→ 可加到 `Document`，仍需开 Issue 讨论形状
   - **业务专属**（合同金额 / 发票号 / 身份证姓名 / 收据条目）→ 由下游业务消费方在自己的聚合根（下游 `Contract` / `Invoice` / `IdCardRecord`）里存储，**`Document` 不污染**

这条规则同时回答"OCR out-of-band 信号该放哪里"——它既不属于下游业务（与具体业务无关）、也不能塞回 Markdown 字符串（破坏 Markdown-first）。它该在 `Document` 层面承载，但每加一种**单独开 Issue**，按需加具名强类型可选字段，**禁止** `Dictionary<string, object>` 通用扩展槽。

## 安全约定（适用于所有内部 LLM 调用路径）

以下安全约定适用于 Paperbase 内部 LLM 调用路径（内置 LLM 分类、Host 字段抽取、租户字段抽取（B 机制）等）：

- **Fail-closed 安全断言**：任何由 LLM 触发或参数受 LLM 输出影响的查询路径，必须显式做**权限断言**（`IAuthorizationService.CheckAsync(...)`）+ 显式 `TenantId` 谓词（**不依赖 ambient `DataFilter`**）+ 结果集硬上限（`Take(N)`），不得裸跑 raw SQL。`DataFilter` 是可读性辅助，不是安全边界——任何禁用过滤器的代码路径（后台任务、单元测试 helper、特殊路径）会绕过保护
- **PromptBoundary**：用户派生的自由文本字段（title / partyName / summary / 文档内容等）进入 LLM prompt 或 LLM-facing 输出前，必须经 `PromptBoundary.WrapField(...)` 包裹，防止 prompt injection 注入向量
- **Description / Instructions 编译期常量**：任何 LLM-facing description / instructions 都必须是**编译期常量**或纯静态字符串字面量，**禁止**运行时拼接用户控制的字符串
- **多租户隔离**：所有 `Document` / 衍生字段查询路径必须显式按 `CurrentTenant.Id` 过滤

具体反例与正确写法见 `.claude/rules/doc-chat-anti-patterns.md`（虽然原文件取材自历史 chat 路径，其中 fail-closed / PromptBoundary / 租户断言模式适用于所有内部 LLM 路径）。

## 处理规则

1. 在 core 中开发时，严格遵循 `.claude/rules/` 中的规则
   - 修改 ABP BackgroundJob / JobArgs 时必须读取 `.claude/rules/background-jobs.md`
2. 模块中不要配置中间件，仅在 host 中配置
3. 遵循 ABP 的依赖注入约定，不要手动调用 `AddScoped` / `AddTransient` / `AddSingleton`
4. **改动前先判断是否需要 Issue**：涉及通道边界（OCR 流水线 / 出口契约 / 字段架构 / 文档类型 Tier 体系 / Markdown-first / 安全约定）、影响模块边界、或属于 Slice 任务的改动，**先停下，告知用户开 GitHub Issue 后再动手**；纯实现细节的 fix（如 bug fix、措辞修正）直接用 commit message 记录即可
5. **分析必须果断**：给结论时先抛**判定**再给**理由**，不要列"可能 A / 也许 B / 取决于你"的菜单把判断推回给用户。两条路都可行时，按项目既定偏好（通道哲学、不重复造轮、瞄准当下与未来）选一条并说明取舍；只有真正无法靠 `grep` / `Read` 在 30 秒内自查的才允许保留不确定性。禁止 hedging 词："可能"、"也许"、"取决于具体情况"、"两种都可以"——除非确实不知道
6. **下游消费方相关问题**：业务模块（合同 / 发票管理等）不属于 Paperbase 范畴。涉及下游消费方实现的讨论，明确指出属于 out-of-scope，Paperbase 只保证出口契约稳定

## 迁移期遗留代码（不要作为新代码范本）

按 [#175](https://github.com/dignite-projects/dignite-paperbase/issues/175) 的依赖项，以下代码在通道定位下属于**待剥离 / 待移除**，**不要**用它们作为新代码或扩展点的设计范本：

| 路径 | 状态 | 跟踪 Issue |
|------|------|-----------|
| `modules/contracts/` | 业务模块整体剥离为下游参考实现 | [#165](https://github.com/dignite-projects/dignite-paperbase/issues/165) |
| `core/src/Dignite.Paperbase.Application/Chat/` | ChatAppService / RAG 问答路径全部移除 | [#166](https://github.com/dignite-projects/dignite-paperbase/issues/166) |
| `core/src/Dignite.Paperbase.Abstractions/Chat/` | 评估是否还需要（业务模块 chat 共享常量在通道定位下不再适用） | [#166](https://github.com/dignite-projects/dignite-paperbase/issues/166) |
| `core/src/Dignite.Paperbase.KnowledgeIndex*/` | 向量化与向量存储不在通道范畴（下游 RAG 基础设施） | 与 [#166](https://github.com/dignite-projects/dignite-paperbase/issues/166) 联动 |

完整剥离与清理计划见 [#165–#175 系列 Issue](https://github.com/dignite-projects/dignite-paperbase/issues?q=is%3Aissue+165..175)。

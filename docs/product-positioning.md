# Paperbase 产品定位与边界设计

> **本文档是产品定位与边界的 truth source（2026-05-17 已固化）**
>
> 这是一份 transitional 决策文档：在 Issue #175（CLAUDE.md 重写）落地之前，本文件 + 11 个相关 Issue（#165–#175）共同构成 Paperbase 新方向的权威描述。#175 完成后，定位与边界将折进 CLAUDE.md，本文件功能由 CLAUDE.md 接管。

---

## Context

经过对照 RAGFlow / Dify / FastGPT / MaxKB / Onyx / AnythingLLM 等开源 RAG 项目反复推敲后确认：

- Paperbase **不**与 RAGFlow 等 RAG 平台正面竞争
- Paperbase 定位为"物理→数字化"通道，给下游消费方（人 / 系统 / AI）提供可信文档基础
- 与 RAGFlow 等 RAG 平台在数据流上是**串联关系**（Paperbase 是上游通道，RAGFlow 是下游消费方之一）

本文件固化产品定位、核心边界、关键设计决策，作为后续 GitHub Issues 拆解和实现的纲领。

---

## 产品定位（一句话）

> **Paperbase = 物理纸质文档 → 可信数字化数据的通道层**

- **入口**：纸张 / 扫描件 / 照片 / PDF 影像 / Office 文件
- **出口**：Markdown + 元数据 + 标准接口（REST / EventBus / MCP server）
- **不消费、不占有、不深入业务**

数据流：

```
物理纸张/扫描件
    ↓
[Paperbase 通道]：OCR + Markdown + 通用元数据 + 可选自定义字段抽取
    ↓ (REST / EventBus / MCP server)
    ├─→ RAGFlow / Dify / 自建 RAG（做 RAG 问答）
    ├─→ 财务系统 / CLM / HR / 弥生 / 用友 / freee 等业务系统
    ├─→ Claude Desktop / Cursor / 任意 MCP 客户端
    └─→ 任何消费方（按需自建 consumer）
```

---

## 设计哲学

1. **集成而非统装**：组件层不重复造轮（OCR / 向量库 / LLM / Markdown 解析都用现成最好的）；差异化在装配方式和产品定位
2. **国际化开源理念**：不走"中国式端到端 SaaS"路径，倾向"组件可换、协议标准、契约清晰"
3. **UNIX 哲学**：do one thing well —— Paperbase 只做物理→数字化通道
4. **使能而非做**：提供基础让下游做事，不亲自做下游的事

---

## 三个 critique（对照 RAGFlow 的设计差异）

| RAGFlow 做法 | Paperbase 做法 | 理由 |
|--------------|---------------|------|
| LLM provider 配置在管理后台（客户配置） | LLM provider 配置在 host 部署层 | 通道哲学下，客户是业务用户不是技术用户 |
| 集成 MCP client（既调用又被调用） | 只做 MCP server | 单一职责，不变成 Agent 平台 |
| DeepDoc 深度绑定不可替换 | OCR provider 可插拔（已有 `Dignite.Paperbase.Ocr` 抽象层） | 集成而非统装 |

---

## 产品边界

### IN scope（在 Paperbase 范围内）

#### 核心管道

- 物理文档接收（扫描件 / 照片 / PDF / Office 等）
- OCR + Layout + 表格识别（通过可插拔 provider）
- Markdown-first 输出 + 溯源元数据（页码 / bbox / 置信度）
- 原件存储 + 处理后数据存储
- 审计日志（合规追溯）

#### 文档分类（三层 type 体系，与字段架构对称）

**文档类型（document type）是字段架构的容器** —— 所有 Tier 1 / Tier 2 字段必须挂在某个文档类型下。

| 层级 | 谁定义 | 范围 | 说明 |
|------|-------|------|------|
| **Tier 0 内置类型** | Paperbase 代码 | 全平台固定 | 合同 / 发票 / 收据 / 报告 / 邮件 / 简历 / 证照 / 通用文档（fallback） |
| **Tier 1 Host 扩展类型** | Host 部署者 | 该部署所有租户共享 | 例：医疗病历 / 学籍档案 / 政策文件 |
| **Tier 2 客户级** | ❌ **不开放** | — | **红线**：防止类型爆炸 + 保持租户一致；Tier 2 只在字段层开放 |

**分类执行机制**：
- 上传时 Paperbase 自动用 LLM 跑分类 prompt → 自动归类
- 置信度低或操作员不同意 → 操作员 UI 可手动修正
- 修正后重新触发后续 pipeline（如对应类型的字段抽取）

#### 三层字段架构（绑定到文档类型）

字段以"文档类型 = 字段集容器"的视角组织——每个 Tier 1 / Tier 2 字段必须挂在某个文档类型下。

| 层级 | 谁定义 | 绑定 | 例子 |
|------|-------|------|------|
| **Tier 0 内置通用字段** | Paperbase 代码 | 所有类型共享（"通用 base"）| filename / size / format / page count / language / OCR confidence / document date / title / summary / topic tags / 通用 NER 实体 / classification |
| **Tier 1 Host 扩展字段** | Host 部署者 | 挂在指定 type 下 | 例：在"医疗病历"type 下加"科室"字段 |
| **Tier 2 客户自定义抽取 (B 机制)** | 客户（per-tenant）| 挂在指定 type 下 | 例：在"合同"type 下加"甲方/乙方/合同金额"。Paperbase 不预置任何业务 schema（红线） |

#### 操作员 UI（"完善管理" 范围）

- 文档：上传 / 列表 / 预览 / 删除 / 重处理
- OCR 流水线：状态 / 队列 / 错误 / 性能监控 / **provider 状态查看**（切换由 host 部署配置，不在 UI 内）
- OCR 结果质量审核：页面对照、置信度查看、手动修正
- 文档分类：查看自动分类结果 / 手动修正分类
- 元数据：查看 / 编辑 / 批量修正
- 标签管理
- **Keyword 全文搜索**（操作员找文档；**故意不提供 NL QA / chat**）
- Metadata filter（按时间 / 类型 / 标签筛选）
- Tier 1 host 扩展字段配置界面（需 host 管理员权限）
- Tier 2 客户自定义字段配置界面（per-tenant）
- 出口订阅管理（哪些 Webhook / MCP 客户端订阅了）
- 自定义导出模板配置（字段映射 + 输出格式）
- 审计日志查看
- 操作员账号 / 权限管理
- **待人工审核队列**：OCR 置信度低 / 分类不确定的文档进入此队列等待操作员处理

#### Host 部署层配置（不在操作员 UI，部署时配置）

- OCR provider 配置（PaddleOCR / Azure DI 等）+ 默认 provider + 兜底链
- Markdown provider 配置（ElBruno MarkItDown 等）
- LLM provider 配置（Azure OpenAI / Anthropic / Ollama 等）
- Tier 1 文档类型扩展
- Tier 1 字段扩展（挂在指定 type 下）
- OCR 置信度阈值（事件发布的最小门槛）
- 事件去重 / 替换策略参数

#### 标准接口（出口）

- **REST API**：完整 CRUD + 查询
- **MCP server**：暴露文档资源 + retrieval tool + `notifications/resources/updated`
- **ABP DistributedEventBus**：薄事件 + 多阶段
- **Webhook**：传统系统消费方式

### 出口事件契约

#### 事件阶段（多阶段 + 薄载荷）

| 阶段事件 | 触发时机 | 受置信度门槛约束 |
|---------|---------|----------------|
| `DocumentUploadedEto` | 文档上传完成 | 否 |
| `OCRCompletedEto` | OCR 完成（含 confidence 指标） | 否 |
| `DocumentClassifiedEto` | 文档分类完成 | 否 |
| `MetadataExtractedEto` | Tier 0 + Tier 1 通用字段抽取完成 | 否 |
| `CustomFieldsExtractedEto` | Tier 2 客户自定义字段抽取完成 | 否 |
| `DocumentReadyEto` | **全流水线完成 + 通过置信度门槛** | **是** |

事件载荷一律薄（ID + 关键元数据），下游通过 REST/MCP 回拉详细数据。

#### OCR 置信度门槛

- **设计意图**：低质量 OCR 不该污染下游
- **门槛执行点**：**仅 `DocumentReadyEto` 受约束**——早期阶段事件正常发，但下游主要消费方默认订阅 DocumentReady，低质量文档不会自动流到 RAG / 业务系统
- **门槛配置**：host 部署级（默认值）+ per-tenant 可覆盖
- **不达标的文档**：
  - 仍然存（不丢失，不删除）
  - 早期阶段事件正常发布
  - **DocumentReady 暂不发**
  - 文档进入操作员 UI 的"待人工审核队列"
  - 操作员修正 / 手动确认通过 → 触发 DocumentReady 发布

#### 事件去重与替换

- **设计意图**：避免重复消费 + 同一份数据迭代更新时不污染下游
- **去重 key**：`(TenantId, DocumentId, EventType)`
- **替换语义**：
  - 同一 key 的事件**未被消费**（in-flight）→ 新事件**替换**旧事件
  - 同一 key 的事件**已被消费** → 发新事件（视作 update）
- **状态追踪**：Paperbase 维护事件状态表（in-flight / consumed）
- **典型场景**：操作员修正 OCR 或字段值 → 重新发 DocumentReady。如果上一版未消费就替换，已消费下游收到 update

### OUT of scope（明确不做）

#### RAG 应用层
- ❌ 向量化（embedding model 选择是下游 RAG 的事）
- ❌ 向量存储（vector DB 是下游 RAG 基础设施）
- ❌ 检索引擎
- ❌ Chat / RAG 问答 / NL search
- ❌ Agent / Workflow 编排（不做 Agent Canvas 类似物）
- ❌ MCP client（不调外部 MCP 工具）
- ❌ 标准化 chunking（chunking 策略让下游 RAG 决定）

#### 业务层
- ❌ 业务字段抽取的预置 schema（合同金额 / 发票号 / 税额等不预置；客户用 (B) 机制自配）
- ❌ 行业 vertical 导入模板的预置（弥生 / 用友 / 金蝶 / freee 等）
  - 但：**客户可用"Tier 2 自定义字段 + 自定义导出模板配置"组合出这些导入格式**
  - Paperbase 不沉淀这些模板，由客户 / 合作伙伴 / 生态维护
- ❌ 业务工作流（审批 / 状态机 / 续签）
- ❌ 业务系统专属连接器（Paperbase 不写"用友连接器"/"弥生连接器"）

#### 配置层
- ❌ 让终端客户配置 LLM provider / API key（host 层配好）
- ❌ 让客户新增文档类型 type（仅 Paperbase 内置 + Host 扩展）

---

## 6 项关键设计决策

| # | 问题 | 决策 |
|---|------|------|
| 1 | 操作员 UI 是否提供 keyword 全文搜索 | ✅ 提供（仅 keyword，无 NL QA / chat） |
| 2 | 通用字段集是固定还是可扩展 | 三层：内置固定 + Host 扩展 + 客户 (B) 自定义 |
| 3 | 自定义字段是出口重组 (A) 还是自定义抽取 (B) | **(B) 自定义抽取引擎**（机制不带 schema） |
| 4 | EventBus 事件载荷 | **薄事件**（ID + 关键元数据，下游回拉详细数据） |
| 5 | EventBus 事件粒度 | **多阶段**（OCRCompleted / Classified / MetadataExtracted / CustomFieldsExtracted / DocumentReady 等） |
| 6 | MCP server 是否也发 notifications | ✅ **双协议**：EventBus（给传统系统）+ MCP `notifications/resources/updated`（给 AI 客户端） |

---

## 关键现有代码资产（继续利用）

- `core/src/Dignite.Paperbase.Ocr/` — OCR provider 抽象层（对应 critique 3，已实现 provider 可插拔）
- `core/src/Dignite.Paperbase.Ocr.PaddleOcr/` — PaddleOCR provider 实现
- `core/src/Dignite.Paperbase.Ocr.AzureDocumentIntelligence/` — Azure DI provider 实现
- `core/src/Dignite.Paperbase.TextExtraction/` — text extraction orchestrator + IMarkdownTextProvider 副契约
- `core/src/Dignite.Paperbase.TextExtraction.ElBrunoMarkItDown/` — Markdown provider 实现
- `core/src/Dignite.Paperbase.Abstractions/` — DocumentTypeDefinition / DocumentClassifiedEto / ITextExtractor 契约
- ABP DistributedEventBus —— 事件出口基础设施已就位
- `Microsoft.Extensions.AI` 接入 —— host 配置 LLM 模式已就位

---

## 需要剥离 / 重构的（明确）

### 剥离出 Paperbase 核心仓库

- `modules/contracts/` —— Contract 业务模块整体（Contract 聚合根 / ContractsSkill / 字段抽取 EventHandler 等）
  - 转为下游"参考实现"项目（独立仓库或独立 examples 目录）
  - 演示如何通过订阅 Paperbase EventBus + 客户 (B) 自定义字段抽取，实现合同档案管理

### Paperbase Core 内部清理

- `core/src/Dignite.Paperbase.Application/Chat/` —— ChatAppService / RAG 问答路径全部移除
- `core/src/Dignite.Paperbase.Application/Documents/Pipelines/` 中的业务字段抽取 workflow —— 改造为通用字段抽取（Tier 0 / Tier 1）+ 客户自定义 (B) 执行引擎
- `core/src/Dignite.Paperbase.Abstractions/Chat/` —— 评估是否还需要（ChatToolNames 等业务模块 chat 共享常量在通道定位下应删除）

### CLAUDE.md 重写（Issue #175）

- 第二层"Modules（业务模块生态）"段落整体重写——业务模块不再是 Paperbase 一部分
- 第三层"Host（宿主应用）"保留，明确 Host 配 LLM 不暴露给客户
- "Markdown-first" 设计原则保留（与通道哲学完全一致）
- "AI 实现约定" 段落删除（Chat / Skill / AgentClassSkill 都不在通道范畴）

---

## 验证标准

### 架构验证
- 下游可以用 RAGFlow 作为消费方接入：RAGFlow 通过 Paperbase MCP server / REST 拿 Markdown + 元数据，自己做 chunking + 向量化 + 检索 + 问答
- 客户通过 (B) 自定义抽取机制实现合同 / 发票 / 任何业务字段抽取，**无需 Paperbase 提供任何业务 schema**
- MCP server 可被 Claude Desktop / Cursor / 任意 MCP 客户端连接和调用
- EventBus 多阶段事件可被传统业务系统（用友 / 弥生等）通过自建 consumer 消费

### 功能验证
- 操作员 UI 完整覆盖：上传 / 状态查看 / 质量审核 / 修正 / 字段配置 / 导出配置 / 审计 / 待人工审核队列
- 文档分类工作正常：上传自动判 + 操作员可修正
- Keyword 全文搜索可工作但故意不提供 NL QA
- 三层字段架构能按预期分别配置和抽取
- DocumentReady 事件受置信度门槛约束（不达标进入待审队列）
- 事件去重与替换工作（in-flight 替换 / 已消费再发 update）

---

## 关联 Issue

本文档对应的实施拆解：

| Issue | 标题 |
|-------|------|
| [#165](https://github.com/dignite-projects/dignite-paperbase/issues/165) | 业务模块剥离：modules/contracts 归档为独立参考实现 |
| [#166](https://github.com/dignite-projects/dignite-paperbase/issues/166) | 移除 Paperbase Core 的 Chat / RAG 问答路径 |
| [#167](https://github.com/dignite-projects/dignite-paperbase/issues/167) | 文档分类机制：Tier 0 内置类型 + LLM 自动分类 + 操作员可修正 |
| [#168](https://github.com/dignite-projects/dignite-paperbase/issues/168) | Tier 1 Host 扩展机制：文档类型 + 字段双扩展 |
| [#169](https://github.com/dignite-projects/dignite-paperbase/issues/169) | Tier 2 客户自定义字段抽取机制（B 机制） |
| [#170](https://github.com/dignite-projects/dignite-paperbase/issues/170) | MCP server 实现 + lifecycle notifications |
| [#171](https://github.com/dignite-projects/dignite-paperbase/issues/171) | EventBus 事件契约：多阶段薄事件 + 置信度门槛 + 去重替换 |
| [#172](https://github.com/dignite-projects/dignite-paperbase/issues/172) | OCR 置信度门槛执行 + 待人工审核队列 |
| [#173](https://github.com/dignite-projects/dignite-paperbase/issues/173) | 操作员 UI 重新设计：含 keyword 全文搜索 + 待审核队列 |
| [#174](https://github.com/dignite-projects/dignite-paperbase/issues/174) | 导出模板与字段映射配置 UI |
| [#175](https://github.com/dignite-projects/dignite-paperbase/issues/175) | CLAUDE.md 重写：通道定位 + 移除业务模块叙事 + 移除 AI 实现约定 |

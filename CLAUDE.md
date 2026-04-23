# Dignite Paperbase

这是一个 ABP 框架的项目，遵循 `.claude/rules/` 中的 ABP 核心约定和模块模板。

## 项目组织

项目分为四个主要目录：
- **core/** - ABP 应用程序核心，遵循 abp-core.md 规则
- **modules/** - 可复用业务模块，每个模块遵循 module-template.md 的结构和虚拟方法要求
- **host/** - 单租户测试主机，仅在此配置中间件（OnApplicationInitialization）
- **docs/** - 项目文档

## 架构设计

Paperbase 采用**三层分离**的模块化架构：

### 第一层：Core（基础设施与能力契约）

`core/` 包含两个核心部分：

1. **Dignite.Paperbase.Abstractions**  
   - **位置**：依赖拓扑的最底层（无其他项目依赖）
   - **职责**：提供扩展契约层，定义业务模块和能力模块接入平台所必需的接口与DTO
   - **内容**：
     - 文档类型注册：`DocumentTypeDefinition`、`DocumentTypeOptions`
     - 集成事件：`DocumentClassifiedEto`
     - 能力端口：`ITextExtractor`、`IDocumentClassifier`、`IFieldExtractor`、`IQaService`、`IEmbeddingIndexer`、`IRelationInferrer`
     - 对应的请求/响应POCO
   - **约束**：不依赖任何其他Paperbase项目，仅依赖ABP基础模块

2. **Dignite.Paperbase（核心模块）与其他能力模块**  
   - **核心职责**：Document 聚合根管理、生命周期编排、事件总线、BackgroundJob 调度
   - **能力模块职责**：TextExtraction、AI（分类/提取/向量/问答/推断）、Ocr
   - **约束**：能力模块仅依赖 Abstractions，不依赖 Domain / Application 的实现；核心不反向依赖任何业务模块

### 第二层：Modules（业务模块生态）

`modules/` 中的各业务模块（如 Contracts）：

- **依赖关系**：仅依赖 `Abstractions` + ABP 基础模块 + `Domain.Shared`（按需，仅消费核心公开的DTO/枚举/本地化）
- **职责**：
  - 通过 `DocumentTypeOptions` 注册自己关心的文档类型
  - 通过 `IDistributedEventBus` 订阅 `DocumentClassifiedEto` 事件
  - 调用 Abstractions 中定义的能力端口（`IFieldExtractor`、`IDocumentClassifier` 等）进行专业领域处理
  - 持久化自己的领域聚合根，提供业务API和UI
  - 按需回写核心搜索索引，注册关系类型
- **非耦合实现**：业务模块之间无依赖；业务模块与核心通过事件和能力契约解耦通信

### 第三层：Host（宿主应用）

`host/` 仅作为容器：

- 在 `[DependsOn(...)]` 中声明依赖的核心模块、能力模块和业务模块
- 在 `ConfigureServices()` 中配置 OCR Provider、AI Provider、存储等运行时选项
- **仅在此处配置中间件**（`OnApplicationInitialization`）
- 不实现任何业务逻辑

### 依赖流向

```
Host Application
    ├── depends on
    │   ├── Dignite.Paperbase（核心）
    │   ├── Dignite.Paperbase.TextExtraction（能力）
    │   ├── Dignite.Paperbase.AI（能力）
    │   └── Dignite.Paperbase.Contracts（业务模块）
    │
    ↓ 所有上层模块汇聚到最底层

Dignite.Paperbase.Abstractions（扩展契约层，无其他项目依赖）
    ├── 被 Core 模块实现和调用
    ├── 被 TextExtraction/AI 模块实现
    └── 被 Contracts 等业务模块引用、注册和订阅
```

**核心约束**：
- **单向依赖**：Abstractions ← 能力模块和业务模块；Paperbase 核心 ⇄ 能力模块不存在反向依赖
- **业务模块间无耦合**：每个业务模块独立开发、独立测试、可独立卸载
- **编排留给核心**：BackgroundJob、PipelineRun 生命周期、Document 的读写都是核心职责

## 处理规则

1. 在 core 和 modules 中开发时，严格遵循 `.claude/rules/` 中的规则
2. 开发可复用模块时，**所有公共和受保护方法必须是虚拟（virtual）的**
3. 模块中不要配置中间件，仅在 host 中配置
4. 遵循 ABP 的依赖注入约定，不要手动调用 AddScoped/AddTransient/AddSingleton
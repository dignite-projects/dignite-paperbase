# Slice 2 — 合同台账基线 实施日志

## Context

Slice 1 完成后 Paperbase 已能完整处理单份合同（上传 → OCR → 分类 → 字段提取 → 列表展示）。Slice 2 的目标是让它成为**可用的合同台账工具**：

- 三要件搜索（到期日 / 金额 / 对方）
- 批量文件上传
- CSV 导出（文档列表 + 合同列表）
- 基础权限角色（ContractManager、Viewer）
- Angular UI 同步更新

**多租户功能明确不做。**

---

## 架构修正：Document 聚合根边界（决策 13）

本切片实施过程中发现并修正了一个架构问题：

**原计划** 在 `Document` 聚合根上添加 `SearchIndexedDate`、`SearchIndexedAmount`、`SearchIndexedParty` 三个字段，由 Contracts 模块通过 `DocumentIndexFieldsUpdatedEto` 事件回写，使核心文档列表能统一支持三要件过滤。

**问题** 这三个字段的含义只在合同语境下成立，放入 `Document` 是把业务概念泄漏到基础设施层。随着业务模块增加，`Document` 会持续膨胀且字段语义歧义无法消除。

**修正** 回滚全部相关代码，确立原则：`Document` 是纯基础设施聚合根，业务查询字段由各业务模块的聚合根自行承载。Contracts 模块通过自己的 `GetContractListInput` 和 Application Service 提供三要件过滤。

该决策已写入：
- `CLAUDE.md`（Document 聚合根边界强制约束）
- `09-架构决策日志.md`（决策 13）
- `07-模块-Dignite.Paperbase.Contracts.md`（§11 改为"合同查询层"）
- `08-业务模块开发指南.md`（§9 改为"业务查询层"）

---

## 已完成的现有基线（不需重建）

| 已存在 | 路径 |
|--------|------|
| `ContractDocumentHandler` 事件处理器 | `modules/contracts/.../EventHandlers/ContractDocumentHandler.cs` |
| `GetContractListInput`（含日期范围 + 对方关键字） | `modules/contracts/.../Dtos/GetContractListInput.cs` |
| `ContractAppService.ApplyFilter`（日期 + 对方过滤） | `modules/contracts/.../ContractAppService.cs` |
| Angular `GetContractListInput` 接口（含日期范围字段） | `modules/contracts/angular/.../contracts.service.ts` |
| Angular `ContractsService.getExportUrl`（含日期参数） | 同上 |

---

## 实施步骤

### Step 1 — 批量上传

**新建**：`core/src/Dignite.Paperbase.Application.Contracts/Documents/BulkUploadResultDto.cs`

```csharp
public class BulkUploadResultDto
{
    public string FileName { get; set; } = default!;
    public Guid? DocumentId { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
}
```

**修改**：`core/src/Dignite.Paperbase.Application.Contracts/Documents/IDocumentAppService.cs`
- 新增 `ExportAsync(GetDocumentListInput input)`

**修改**：`core/src/Dignite.Paperbase.HttpApi/Documents/DocumentController.cs`
- 新增 `POST /api/paperbase/documents/bulk-upload`（接收 `IFormFileCollection`，逐文件调用 `UploadAsync`，收集每个文件的成功/失败结果）
- 新增 `GET /api/paperbase/documents/export`

**修改**：`core/src/Dignite.Paperbase.Application/Documents/DocumentAppService.cs`
- 实现 `ExportAsync`（标准库手写 CSV，列：`Id, DocumentTypeCode, LifecycleStatus, OriginalFileName, ContentType, CreationTime`）
- `GetDocumentListInput.LifecycleStatus` 改为枚举类型（之前为 `string?`）

---

### Step 2 — Document CSV 导出权限

**修改**：`core/src/Dignite.Paperbase.Application.Contracts/Permissions/PaperbasePermissions.cs`
```csharp
public const string Export = Default + ".Export";
```

**修改**：`PaperbasePermissionDefinitionProvider.cs` — 注册 `Export` 权限子节点

---

### Step 3 — Contracts 模块 CSV 导出权限

**修改**：`modules/contracts/src/.../Permissions/ContractsPermissions.cs`
```csharp
public const string Export = Default + ".Export";
```

**修改**：`ContractsPermissionDefinitionProvider.cs` — 注册导出权限

**修改**：`modules/contracts/src/.../IContractAppService.cs`
- 新增 `ExportAsync(GetContractListInput input)`

**修改**：`modules/contracts/src/.../ContractAppService.cs`
- 实现 `ExportAsync` + `BuildContractCsv`（列：`Id, DocumentId, DocumentTypeCode, Title, ContractNumber, CounterpartyName, SignedDate, ExpirationDate, TotalAmount, Currency, Status, NeedsReview`）

**修改**：`modules/contracts/src/.../ContractController.cs`
- 新增 `GET /api/paperbase/contracts/export`

---

### Step 4 — 合同金额范围过滤（三要件完整化）

**修改**：`modules/contracts/src/.../Dtos/GetContractListInput.cs`
```csharp
public decimal? TotalAmountMin { get; set; }
public decimal? TotalAmountMax { get; set; }
```

**修改**：`ContractAppService.ApplyFilter` — 追加金额范围条件

---

### Step 5 — 权限角色种子数据

**新建**：`host/src/Data/PaperbaseRoleDataSeedContributor.cs`

| 角色 | 权限 |
|------|------|
| `ContractManager` | `Documents.Default`, `Documents.Upload`, `Documents.Export`, `Contracts.Contracts`, `Contracts.Contracts.Update`, `Contracts.Contracts.Confirm`, `Contracts.Contracts.Export` |
| `Viewer` | `Documents.Default`, `Contracts.Contracts` |

---

### Step 6 — Angular：Document 列表 UI

**修改**：`host/angular/src/app/proxy/models.ts`
- `GetDocumentListInput` 仅保留 `LifecycleStatus`、`DocumentTypeCode`（不含搜索索引字段）
- 新增 `BulkUploadResultDto` 接口

**修改**：`host/angular/src/app/proxy/document.service.ts`
- 新增 `bulkUpload(files: File[])`
- 新增 `getExportUrl(input: GetDocumentListInput)`

**修改**：`host/angular/src/app/document/document-list/document-list.component.ts`
- 新增 `isBulkUploading`、`bulkUploadResults` 状态
- 新增 `onBulkFileChange()`、`exportCsv()` 方法

**修改**：`host/angular/src/app/document/document-list/document-list.component.html`
- 工具栏新增"导出 CSV"按钮
- 新增多文件选择输入 + 批量上传结果面板

---

### Step 7 — Angular：合同列表搜索面板

**修改**：`modules/contracts/angular/.../services/contracts.service.ts`
- `GetContractListInput` 接口新增 `amountMin?`、`amountMax?`
- `getExportUrl` 新增金额参数传递

**修改**：`modules/contracts/angular/.../components/contracts.component.ts`
- 新增 `expirationDateFrom`、`expirationDateTo`、`amountMin`、`amountMax` 状态字段
- `load()` 和 `exportCsv()` 传递完整过滤参数
- 模板新增到期日范围（date picker × 2）和金额范围（number input × 2）输入框

---

## 关键文件速查

| 文件 | 改动类型 |
|------|----------|
| `core/.../Documents/BulkUploadResultDto.cs` | 新建 |
| `core/.../Documents/IDocumentAppService.cs` | 新增 ExportAsync |
| `core/.../Permissions/PaperbasePermissions.cs` | 新增 Export |
| `core/.../Permissions/PaperbasePermissionDefinitionProvider.cs` | 注册 Export |
| `core/.../Application/Documents/DocumentAppService.cs` | ExportAsync + ApplyFilter 修正 |
| `core/.../HttpApi/Documents/DocumentController.cs` | bulk-upload + export 端点 |
| `modules/contracts/.../Dtos/GetContractListInput.cs` | 新增 TotalAmountMin/Max |
| `modules/contracts/.../Permissions/ContractsPermissions.cs` | 新增 Export |
| `modules/contracts/.../IContractAppService.cs` | 新增 ExportAsync |
| `modules/contracts/.../ContractAppService.cs` | ExportAsync + 金额过滤 |
| `modules/contracts/.../ContractController.cs` | export 端点 |
| `modules/contracts/.../EventHandlers/ContractDocumentHandler.cs` | 移除 IDistributedEventBus（决策 13 回滚） |
| `host/src/Data/PaperbaseRoleDataSeedContributor.cs` | 新建（角色种子） |
| `host/angular/.../document-list/document-list.component.*` | 批量上传 + 导出 UI |
| `host/angular/.../proxy/document.service.ts` | bulkUpload + getExportUrl |
| `modules/contracts/angular/.../contracts.service.ts` | 金额字段 + getExportUrl |
| `modules/contracts/angular/.../contracts.component.ts` | 搜索面板完整化 |
| `CLAUDE.md` | Document 聚合根边界约束 |

---

## 验证方式

1. `dotnet build`（`host/src`）— 0 个错误
2. 调用 `POST /api/paperbase/documents/bulk-upload` 上传 3 个文件，验证返回每个文件的 `succeeded` / `errorMessage`
3. 调用 `GET /api/paperbase/documents/export` — 下载 CSV，确认列完整
4. 调用 `GET /api/paperbase/contracts?totalAmountMin=10000` — 验证只返回金额 ≥ 10000 的合同
5. 调用 `GET /api/paperbase/contracts/export?counterpartyKeyword=ABC&expirationDateFrom=2025-01-01` — 下载 CSV 验证过滤一致
6. 用 `Viewer` 角色调用 Upload/Export — 应返回 403
7. Angular 合同列表：输入到期日范围 + 金额范围 + 对方关键字，点击搜索，结果正确；导出 CSV 携带过滤条件

---

## 明确不做（本切片范围外）

- 多租户数据隔离
- AI 字段提取（Slice 3）
- 分类置信度低时的人工复核 UI
- 文档关系图谱
- MCP 工具接入
- Embedding 流水线

# Documents/Pipelines/RelationInference/

预留目录。`DocumentRelationInferenceWorkflow` 与 `DocumentRelationInferenceBackgroundJob`
将放在此处，命名空间 `Dignite.Paperbase.Documents.Pipelines.RelationInference`。

设计意图见 `CLAUDE.md` § "AI 实现约定"——文档加工流水线按业务能力垂直切片，关系推断
作为第三条 LLM 流水线（与 Classification / Embedding 平级）独立成目录。

实现该 Workflow 时：
- 落在 `Documents/Pipelines/RelationInference/DocumentRelationInferenceWorkflow.cs`
- 提示词通过 `Application/Ai/IPromptProvider` 取得（不要直接写常量）
- LLM 失败兜底策略由 `DocumentRelationInferenceBackgroundJob` 决定（参考
  `Documents/Pipelines/Classification/DocumentClassificationBackgroundJob` 的兜底模式）
- 配置项加在 `Application/Ai/PaperbaseAIBehaviorOptions`（绑定到 `appsettings.json` 的 `PaperbaseAIBehavior` 节）
- 接受 `maf-workflow-reviewer` agent 审查（详见 `.claude/agents/maf-workflow-reviewer.md`）

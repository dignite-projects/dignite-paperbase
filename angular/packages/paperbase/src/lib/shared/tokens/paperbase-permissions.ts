export const PAPERBASE_PERMISSIONS = {
  Documents: {
    Default: 'Paperbase.Documents',
    Upload: 'Paperbase.Documents.Upload',
    Delete: 'Paperbase.Documents.Delete',
    Restore: 'Paperbase.Documents.Restore',
    Export: 'Paperbase.Documents.Export',
    ConfirmClassification: 'Paperbase.Documents.ConfirmClassification',
    Chat: {
      Default: 'Paperbase.Documents.Chat',
      Create: 'Paperbase.Documents.Chat.Create',
      SendMessage: 'Paperbase.Documents.Chat.SendMessage',
      Delete: 'Paperbase.Documents.Chat.Delete',
    },
    Pipelines: {
      Default: 'Paperbase.Documents.Pipelines',
      Retry: 'Paperbase.Documents.Pipelines.Retry',
    },
  },
  DocumentRelations: {
    Default: 'Paperbase.DocumentRelations',
    Create: 'Paperbase.DocumentRelations.Create',
    Delete: 'Paperbase.DocumentRelations.Delete',
    ConfirmRelation: 'Paperbase.DocumentRelations.ConfirmRelation',
  },
} as const;

export const PAPERBASE_PERMISSIONS = {
  Documents: {
    Default: 'Paperbase.Documents',
    Upload: 'Paperbase.Documents.Upload',
    Delete: 'Paperbase.Documents.Delete',
    Restore: 'Paperbase.Documents.Restore',
    Export: 'Paperbase.Documents.Export',
    ConfirmClassification: 'Paperbase.Documents.ConfirmClassification',
    Pipelines: {
      Default: 'Paperbase.Documents.Pipelines',
      Retry: 'Paperbase.Documents.Pipelines.Retry',
    },
  },
  Chat: {
    Default: 'Paperbase.Chat',
    Create: 'Paperbase.Chat.Create',
    SendMessage: 'Paperbase.Chat.SendMessage',
    Delete: 'Paperbase.Chat.Delete',
  },
  DocumentRelations: {
    Default: 'Paperbase.DocumentRelations',
    Create: 'Paperbase.DocumentRelations.Create',
    Delete: 'Paperbase.DocumentRelations.Delete',
    ConfirmRelation: 'Paperbase.DocumentRelations.ConfirmRelation',
  },
} as const;

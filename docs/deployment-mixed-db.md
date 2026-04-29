# Qdrant Deployment Notes

This repository no longer uses a separate relational RAG database. The open-source host stores document knowledge in Qdrant through `Dignite.Paperbase.Rag.Qdrant`.

## Topology

```text
Paperbase Host
  -> PostgreSQL application database
  -> Qdrant collection: paperbase_document_chunks
```

The relational database keeps Paperbase business entities. Qdrant keeps chunk embeddings and payload fields used for filtered RAG search.

## Configuration

Relational database:

```json
"ConnectionStrings": {
  "Default": "Host=db-main;Port=5432;Database=paperbase;Username=paperbase_app;Password=__SET_FROM_SECRETS__",
  "Paperbase": "Host=db-main;Port=5432;Database=paperbase;Username=paperbase_app;Password=__SET_FROM_SECRETS__"
}
```

Qdrant:

```json
"QdrantRag": {
  "Endpoint": "http://qdrant:6334",
  "ApiKey": "",
  "CollectionName": "paperbase_document_chunks",
  "Distance": "Cosine",
  "VectorDimension": 1536,
  "EnsureCollectionOnStartup": true
}
```

See `host/src/appsettings.Qdrant.Sample.json` and `host/docker-compose.yml`.

## Migration Boundary

Qdrant has no EF Core migration history. On startup, `QdrantRagModule` validates or creates the configured collection and payload indexes.

When changing embedding dimensions, create a new Qdrant collection or recreate the existing one, then re-run document embedding jobs.

## Delete Semantics

Document delete cleanup is after-commit:

- the relational document transaction commits first,
- then the Application-layer delete event handler calls `IDocumentKnowledgeIndex.DeleteByDocumentIdAsync`,
- Qdrant deletion uses `tenant_id + document_id` filters.

This avoids deleting Qdrant points for a relational transaction that later rolls back.

using System;

namespace Dignite.Paperbase.Rag.Qdrant;

internal static class QdrantPayloadEncoder
{
    public static string EncodeTenantId(Guid? tenantId)
        => tenantId?.ToString("D") ?? QdrantPayloadFields.HostTenantId;

    public static string EncodeDocumentId(Guid documentId)
        => documentId.ToString("D");
}

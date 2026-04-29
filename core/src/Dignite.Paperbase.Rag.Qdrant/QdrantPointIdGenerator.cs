using System;
using System.Security.Cryptography;
using System.Text;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Rag.Qdrant;

public class QdrantPointIdGenerator : ITransientDependency
{
    public virtual Guid Create(Guid? tenantId, Guid documentId, int chunkIndex)
    {
        var key = string.Join(
            "|",
            QdrantPayloadEncoder.EncodeTenantId(tenantId),
            QdrantPayloadEncoder.EncodeDocumentId(documentId),
            chunkIndex.ToString());

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);

        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }
}

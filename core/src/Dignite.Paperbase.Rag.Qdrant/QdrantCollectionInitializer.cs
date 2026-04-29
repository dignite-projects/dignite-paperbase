using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Rag.Qdrant;

public class QdrantCollectionInitializer : ITransientDependency
{
    private readonly IQdrantClientGateway _gateway;
    private readonly QdrantRagOptions _options;

    public QdrantCollectionInitializer(
        IQdrantClientGateway gateway,
        IOptions<QdrantRagOptions> options)
    {
        _gateway = gateway;
        _options = options.Value;
    }

    public virtual async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EnsureCollectionOnStartup)
        {
            return;
        }

        await _gateway.EnsureCollectionAsync(_options, cancellationToken);
    }
}

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.KnowledgeIndex.Qdrant;

public class QdrantClientFactory : ISingletonDependency
{
    private readonly QdrantKnowledgeIndexOptions _options;
    private readonly ILoggerFactory _loggerFactory;

    public QdrantClientFactory(
        IOptions<QdrantKnowledgeIndexOptions> options,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
    }

    public virtual IQdrantClient CreateClient()
    {
        var endpoint = NormalizeEndpoint(_options.Endpoint);
        return new QdrantClient(endpoint, _options.ApiKey, loggerFactory: _loggerFactory);
    }

    protected virtual Uri NormalizeEndpoint(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        if (Uri.TryCreate("http://" + endpoint, UriKind.Absolute, out uri))
        {
            return uri;
        }

        throw new InvalidOperationException("QdrantKnowledgeIndex:Endpoint must be an absolute URI or host:port value.");
    }
}

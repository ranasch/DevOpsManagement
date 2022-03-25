using Microsoft.Azure.WebJobs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsManagement.Test.Helper;

internal class AsyncCollector<T> : IAsyncCollector<T>
{
    public readonly List<T> Items = new List<T>();

    public Task AddAsync(T item, CancellationToken cancellationToken = default(CancellationToken))
    {

        Items.Add(item);

        return Task.FromResult(true);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
        return Task.FromResult(true);
    }
}

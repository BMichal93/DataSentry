using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataSentry.Tests;

/// <summary>Drains a stream into a list, so a test can assert on what came out of it.</summary>
internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var items = new List<T>();

        await foreach (T item in source)
        {
            items.Add(item);
        }

        return items;
    }
}

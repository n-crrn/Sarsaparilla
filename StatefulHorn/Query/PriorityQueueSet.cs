using System.Collections.Generic;

namespace StatefulHorn.Query;

/// <summary>
/// The interface that must be implemented by items added to a PriorityQueueSet. This
/// interface allows the queue to extract the priority of the item from the item
/// itself.
/// </summary>
public interface IPriorityQueueSetItem
{

    /// <summary>
    /// Priority of the item, lowest value is priority for earliest retrieval.
    /// </summary>
    public int Priority { get; }

}

/// <summary>
/// A combination priority queue and set class - that is, a priority queue where items can only
/// be added once at a time to the queue.
/// </summary>
/// <typeparam name="T">Type implementing IPriorityQueueSetItem.</typeparam>
public class PriorityQueueSet<T> where T : notnull, IPriorityQueueSetItem
{

    /// <summary>
    /// Internal queue for determining the ordering of items.
    /// </summary>
    private readonly PriorityQueue<T, int> Ordering = new();

    /// <summary>
    /// Set for quickly determining if an item has previously been added.
    /// </summary>
    private readonly HashSet<T> Contents = new();

    /// <summary>
    /// The number of items in the PriorityQueueSet.
    /// </summary>
    public int Count => Ordering.Count;

    /// <summary>
    /// Extract the next item with the lowest-valued priority. The return order of items with the
    /// same priority is not guaranteed consistent in any respect.
    /// </summary>
    /// <returns>
    /// Item that had been previously queued. An exception is thrown if there are no items in the
    /// queue.
    /// </returns>
    public T? Dequeue()
    {
        T item = Ordering.Dequeue();
        Contents.Remove(item);
        return item;
    }

    /// <summary>
    /// Add an item to the queue.
    /// </summary>
    /// <param name="item">Item to add.</param>
    public void Enqueue(T item)
    {
        if (Contents.Add(item))
        {
            Ordering.Enqueue(item, item.Priority);
        }
    }

}

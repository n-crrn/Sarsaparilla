using System;
using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn.Query;

public class QueueSet<T> where T : notnull
{

    private readonly LinkedList<T> Ordering = new();

    private readonly HashSet<T> Contents = new();

    public int Count => Ordering.Count;

    public T? Dequeue()
    {
        T item = Ordering.First();
        Ordering.RemoveFirst();
        Contents.Remove(item);
        return item;
    }

    public void Enqueue(T item)
    {
        if (Contents.Add(item))
        {
            Ordering.AddLast(item);
        }
    }

    public void RemoveAll(Predicate<T> p)
    {
        LinkedListNode<T>? nextNode = Ordering.First;
        while (nextNode != null)
        {
            LinkedListNode<T>? followingNode = nextNode.Next;
            if (p.Invoke(nextNode.Value))
            {
                Contents.Remove(nextNode.Value);
                Ordering.Remove(nextNode);
            }
            nextNode = followingNode;
        }

    }

}

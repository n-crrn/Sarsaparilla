using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace AppliedPi;

public class GroupSet<T> : IEnumerable<IReadOnlySet<T>> where T : notnull
{

    public GroupSet() { }

    public GroupSet(IEnumerable<HashSet<T>> sets)
    {
        Groups.AddRange(from hs in sets select new HashSet<T>(hs));
    }

    public GroupSet(params T[] firstGroup)
    {
        Groups.Add(new(firstGroup));
    }

    private readonly List<HashSet<T>> Groups = new();

    public void Add(T item)
    {
        // Quick check to ensure that it is not already held.
        HashSet<T>? g = InnerGetGroup(item);
        if (g == null)
        {
            Groups.Add(new() { item });
        }
    }

    public void Add(T item1, T item2)
    {
        HashSet<T>? g1 = InnerGetGroup(item1);
        HashSet<T>? g2 = InnerGetGroup(item2);

        if (g1 != null && g2 != null)
        {
            // The two sets need to be merged.
            Groups.Remove(g2);
            g1.UnionWith(g2);
        }
        else if (g1 != null && g2 == null)
        {
            g1.Add(item2);
        }
        else if (g1 == null && g2 != null)
        {
            g2.Add(item1);
        }
        else
        {
            Groups.Add(new() { item1, item2 });
        }
    }

    public IReadOnlySet<T>? GetGroup(T sampleItem) => InnerGetGroup(sampleItem);

    private HashSet<T>? InnerGetGroup(T sampleItem)
    {
        foreach (HashSet<T> g in Groups)
        {
            if (g.Contains(sampleItem))
            {
                return g;
            }
        }
        return null;
    }

    public void UnionWith(GroupSet<T> other)
    {
        foreach (HashSet<T> g in other.Groups)
        {
            IEnumerator<T> msgIter = g.GetEnumerator();
            msgIter.MoveNext();
            T first = msgIter.Current;
            Add(first);
            while (msgIter.MoveNext())
            {
                Add(first, msgIter.Current);
            }
        }
    }

    public bool ValidateSets(Func<T, T, bool> validator)
    {
        foreach (HashSet<T> g in Groups)
        {
            List<T> gAsList = g.ToList();
            for (int i = 0; i < gAsList.Count; i++)
            {
                for (int j = i + 1; j < gAsList.Count; j++)
                {
                    if (!validator(gAsList[i], gAsList[j]))
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    #region IEnumerable implementation.

    private IEnumerator<IReadOnlySet<T>> InnerGetEnumerator()
    {
        foreach (HashSet<T> g in Groups)
        {
            yield return g;
        }
    }

    public IEnumerator<IReadOnlySet<T>> GetEnumerator() => InnerGetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => InnerGetEnumerator();

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        if (obj is not GroupSet<T> gst || Groups.Count != gst.Groups.Count)
        {
            return false;
        }
        for (int i = 0; i < Groups.Count; i++)
        {
            bool found = false;
            for (int j = 0; j < gst.Groups.Count; j++)
            {
                if (Groups[i].SetEquals(gst.Groups[j]))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                return false;
            }
        }
        return true;
    }

    // A bit half-baked, but anything proper would be too computationally intensive.
    public override int GetHashCode() => (from g in Groups select g.Count).Sum();

    public override string ToString()
    {
        List<string> groupStr = new();
        foreach (HashSet<T> g in Groups)
        {
            groupStr.Add("{" + string.Join(", ", g) + "}");
        }
        return "{ " + string.Join("; ", groupStr) + " }";
    }

    #endregion

}

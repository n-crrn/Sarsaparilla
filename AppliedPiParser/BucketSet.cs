using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace AppliedPi;

/// <summary>
/// This is an Abstract Data Type designed to allow values to be allocated to a bucket (set) based
/// on a key. This pattern comes up multiple times in the AppliedPi library.
/// </summary>
public class BucketSet<TKey, TValue> : IEnumerable<(TKey, IReadOnlySet<TValue>)> where TKey : notnull 
{

    #region Constructors.

    /// <summary>Create a new, empty BucketSet.</summary>
    public BucketSet() { }

    /// <summary>
    /// Create a new BucketSet with one key pointing to one bucket containing only the given value.
    /// </summary>
    /// <param name="k">The key.</param>
    /// <param name="v">The value.</param>
    public BucketSet(TKey k, TValue v)
    {
        Add(k, v);
    }

    /// <summary>
    /// Create a new BucketSet using an enumerable of tuples of keys and values.
    /// </summary>
    /// <param name="initialItems">Enumerable of tuples.</param>
    public BucketSet(IEnumerable<(TKey, TValue)> initialItems)
    {
        foreach ((TKey k, TValue v) in initialItems)
        {
            Add(k, v);
        }
    }

    /// <summary>
    /// Create a new BucketSet using an enumerable of tuples of keys and enumerables containing
    /// values.
    /// </summary>
    /// <param name="initialBuckets">Initial values.</param>
    public BucketSet(IEnumerable<(TKey, IEnumerable<TValue>)> initialBuckets)
    {
        foreach ((TKey k, IEnumerable<TValue> vSet) in initialBuckets)
        {
            _Buckets[k] = new(vSet);
        }
    }

    /// <summary>
    /// Create a new BucketSet containing the same values as other. All containing structures
    /// (e.g. dictionaries and sets) are duplicated as part of the creation.
    /// </summary>
    /// <param name="other">The BucketSet to duplicate.</param>
    public BucketSet(BucketSet<TKey, TValue> other)
    {
        foreach ((TKey k, HashSet<TValue> v) in other._Buckets)
        {
            _Buckets[k] = new(v);
        }
    }

    #endregion
    #region Properties.

    /// <summary>The editable Buckets member.</summary>
    private readonly Dictionary<TKey, HashSet<TValue>> _Buckets = new();

    /// <summary>
    /// Provides public access to the Buckets list. External users are expected to not add or 
    /// remove items from individual buckets.
    /// </summary>
    public IReadOnlyDictionary<TKey, HashSet<TValue>> Buckets => _Buckets;

    /// <summary>The total number of items held by the BucketSet in every bucket.</summary>
    public int Count => (from bv in _Buckets.Values select bv.Count).Sum();

    #endregion
    #region Adding and querying items.

    /// <summary>
    /// Add the given key and value pair to the BucketSet. A new bucket is created if necessary.
    /// </summary>
    /// <param name="k">Key.</param>
    /// <param name="v">Value.</param>
    public void Add(TKey k, TValue v)
    {
        if (_Buckets.TryGetValue(k, out HashSet<TValue>? bucket))
        {
            bucket!.Add(v);
        }
        else
        {
            _Buckets[k] = new() { v };
        }
    }

    /// <summary>
    /// Creates a union of two BucketSets. All key and value pairs of other are incorporated into
    /// this BucketSet.
    /// </summary>
    /// <param name="other">BucketSet with values to copy.</param>
    public void UnionWith(BucketSet<TKey, TValue> other)
    {
        foreach ((TKey k, IEnumerable<TValue> vSet) in other)
        {
            if (_Buckets.TryGetValue(k, out HashSet<TValue>? bucket))
            {
                bucket!.UnionWith(vSet);
            }
            else
            {
                _Buckets[k] = new(vSet);
            }
        }
    }

    /// <summary>
    /// Returns true if the key k is held within this BucketSet.
    /// </summary>
    /// <param name="k">Key.</param>
    /// <returns>True if key held.</returns>
    public bool ContainsKey(TKey k) => _Buckets.ContainsKey(k);

    /// <summary>Accesses buckets based on key.</summary>
    /// <param name="key">Key of set to retrieve.</param>
    /// <returns>The set indicated by key.</returns>
    public IReadOnlySet<TValue> Get(TKey key) => _Buckets[key];

    /// <summary>Indexer for accessing buckets by key.</summary>
    /// <param name="key">Key of set to retrieve.</param>
    /// <returns>The set indicated by key.</returns>
    public IReadOnlySet<TValue> this[TKey key] => _Buckets[key];

    #endregion
    #region IEnumerable implementation.

    /// <summary>
    /// A coroutine for iterating through the buckets of the BucketSet.
    /// </summary>
    /// <returns>Enumerator that returns a tuple consisting of the key and the value.</returns>
    private IEnumerator<(TKey, IReadOnlySet<TValue>)> InnerGetEnumerator()
    {
        foreach ((TKey k, HashSet<TValue> vSet) in _Buckets)
        {
            yield return (k, vSet.ToImmutableHashSet());
        }
    }

    public IEnumerator<(TKey, IReadOnlySet<TValue>)> GetEnumerator() => InnerGetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => InnerGetEnumerator();

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? other)
    {
        if (other is BucketSet<TKey, TValue> bs)
        {
            if (_Buckets.Count != bs._Buckets.Count)
            {
                return false;
            }
            foreach ((TKey k, IReadOnlySet<TValue> vSet) in bs)
            {
                if (!_Buckets.TryGetValue(k, out HashSet<TValue>? thisVSet) || thisVSet!.Count != vSet.Count)
                {
                    return false;
                }
                if (!thisVSet!.SetEquals(vSet))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    public override int GetHashCode()
    {
        List<TKey> keys = new(_Buckets.Keys);
        keys.Sort();
        int hc = 7901;
        foreach (TKey k in keys)
        {
            hc = hc * 7907 + k.GetHashCode();
        }
        return hc;
    }

    public override string ToString()
    {
        List<string> entries = new();
        foreach ((TKey k, IReadOnlySet<TValue> vSet) in _Buckets)
        {
            string inner = string.Join(", ", vSet);
            entries.Add($"{k} = [{inner}]");
        }
        return string.Join(", ", entries);
    }

    #endregion

}

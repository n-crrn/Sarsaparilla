using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace AppliedPi;

/// <summary>
/// This is an Abstract Data Type designed to allow values to be allocated to a bucket based on
/// a key. This pattern comes up multiple times in the AppliedPi library.
/// </summary>
public class BucketSet<TKey, TValue> : IEnumerable<(TKey, IReadOnlySet<TValue>)> where TKey : notnull 
{

    public BucketSet() { }

    public BucketSet(IEnumerable<(TKey, TValue)> initialItems)
    {
        foreach ((TKey k, TValue v) in initialItems)
        {
            Add(k, v);
        }
    }

    public BucketSet(IEnumerable<(TKey, IEnumerable<TValue>)> initialBuckets)
    {
        foreach ((TKey k, IEnumerable<TValue> vSet) in initialBuckets)
        {
            _Buckets[k] = new(vSet);
        }
    }

    public BucketSet(BucketSet<TKey, TValue> other)
    {
        foreach ((TKey k, HashSet<TValue> v) in other._Buckets)
        {
            _Buckets[k] = new(v);
        }
    }

    private readonly Dictionary<TKey, HashSet<TValue>> _Buckets = new();

    public IReadOnlyDictionary<TKey, HashSet<TValue>> Buckets => _Buckets;

    public int Count => (from bv in _Buckets.Values select bv.Count).Sum();

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

    public bool ContainsKey(TKey k) => _Buckets.ContainsKey(k);

    public IReadOnlySet<TValue> Get(TKey key) => _Buckets[key];

    public IReadOnlySet<TValue> this[TKey key] => _Buckets[key];

    #region IEnumerable implementation.

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

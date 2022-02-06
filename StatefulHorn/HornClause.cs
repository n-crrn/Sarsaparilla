using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatefulHorn;

public class HornClause
{
    public HornClause(IMessage result, IEnumerable<IMessage> premises)
    {
        Premises = new HashSet<IMessage>(premises);
        Result = result;
        CalculateHashCode();
    }

    public IReadOnlySet<IMessage> Premises { get; init; }

    public IMessage Result { get; init; }

    public bool IsKnown(HashSet<IMessage> knowledge) => Premises.All((IMessage m) => knowledge.Contains(m));

    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is HornClause hc &&
            Result.Equals(hc.Result) &&
            Premises.Count == hc.Premises.Count &&
            Premises.SetEquals(hc.Premises);
    }

    private int Hash;

    private void CalculateHashCode()
    {
        const int init = 673;
        const int diff = 839;
        Hash = init * diff + Result.GetHashCode();
        // Note that the following code relies on a consistent ordering of retrieval of the premises.
        foreach (IMessage msg in Premises)
        {
            Hash = Hash * diff + msg.GetHashCode();
        }
    }

    public override int GetHashCode() => Hash;

    #endregion
}

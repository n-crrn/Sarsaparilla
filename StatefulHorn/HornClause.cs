using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StatefulHorn.Messages;
using StatefulHorn.Origin;

namespace StatefulHorn;

public class HornClause
{
    public HornClause(IMessage result, IEnumerable<IMessage> premises, Guard? guard = null)
    {
        Guard = guard ?? Guard.Empty;
        Premises = new HashSet<IMessage>(premises);
        Result = result;

        int premiseComplexity = Premises.Count == 0 ? 0 : (from p in Premises select p.FindMaximumDepth()).Max();
        int resultComplexity = Result.FindMaximumDepth();
        Complexity = Math.Max(premiseComplexity, resultComplexity);
        IncreasesComplexity = premiseComplexity < resultComplexity;
        DeceasesComplexity = premiseComplexity > resultComplexity;

        CalculateHashCode();
        CollectVariables();
        Debug.Assert(Variables != null);
    }

    public HornClause Substitute(SigmaMap map)
    {
        if (map.IsEmpty)
        {
            return Clone();
        }
        IMessage updatedResult = Result.PerformSubstitution(map);
        HornClause hc = new(updatedResult, from p in Premises select p.PerformSubstitution(map))
        {
            Parent = this,
            Rank = Rank,
            Source = new SubstitutionRuleSource(this, map),
            Guard = Guard.PerformSubstitution(map)
        };
        return hc;
    }

    public IEnumerable<HornClause> DetupleResult()
    {
        if (Result is TupleMessage tMsg)
        {
            foreach (IMessage member in tMsg.Members)
            {
                HornClause innerHC = new(member, Premises)
                {
                    Parent = Parent ?? this,
                    Rank = Rank,
                    Source = new OperationRuleSource(this, OperationRuleSource.Op.Detuple),
                    Guard = Guard
                };
                foreach (HornClause deepHC in innerHC.DetupleResult())
                {
                    yield return deepHC;
                }
            }
        }
        else
        {
            yield return this;
        }
    }

    private HornClause Clone()
    {
        HornClause hc = new(Result, Premises)
        {
            Parent = Parent,
            Rank = Rank,
            Source = Source,
            Guard = Guard
        };
        return hc;
    }

    public List<HornClause>? ComposeUpon(HornClause other)
    {
        if (!ComplexResult || !BeforeRank(other.Rank))
        {
            return null;
        }

        List<HornClause> found = new();
        foreach (IMessage msg in other.Premises)
        {
            if (msg is not VariableMessage && msg.GetType() == Result.GetType())
            {
                SigmaFactory sf = new();
                if (Result.DetermineUnifiableSubstitution(msg, Guard.Empty, sf) &&
                    sf.ForwardIsValidByGuard(Guard) && 
                    sf.BackwardIsValidByGuard(other.Guard))
                {
                    SigmaMap fwdMap = sf.CreateForwardMap();
                    SigmaMap bwdMap = sf.CreateBackwardMap();

                    HornClause thisUpdated = Substitute(fwdMap);
                    IEnumerable<IMessage> oPremises = (from op in other.Premises where !op.Equals(msg) select op.PerformSubstitution(bwdMap)).Concat(thisUpdated.Premises);
                    IMessage oResult = other.Result.PerformSubstitution(bwdMap);
                    HornClause otherUpdated = new(oResult, oPremises)
                    {
                        Guard = Guard.PerformSubstitution(fwdMap).Union(other.Guard.PerformSubstitution(bwdMap))
                    };
                    
                    // Final check - ensure result not in premise.
                    if (!otherUpdated.Premises.Contains(otherUpdated.Result))
                    {
                        otherUpdated.Parent = this;
                        otherUpdated.Rank = RatchetRank(Rank, other.Rank);
                        otherUpdated.Source = new CompositionRuleSource(this, other);
                        found.Add(otherUpdated);
                    }
                }
            }
        }
        return found.Count == 0 ? null : found;
    }

    #region Properties.

    public Guard Guard { get; init; }

    public IReadOnlySet<IMessage> Premises { get; init; }

    public IMessage Result { get; init; }

    public bool ComplexResult => Result is FunctionMessage || Result is TupleMessage;

    public HornClause? Parent { get; private set; }

    public int Rank { get; set; } = -1;

    public bool BeforeRank(int r) => Rank == -1 || r == -1 || Rank <= r;

    internal static int RatchetRank(int r1, int r2)
    { 
        if (r1 == -1)
        {
            return r2;
        }
        if (r2 == -1)
        {
            return r1;
        }
        return Math.Min(r1, r2);
    }

    public IRuleSource? Source { get; set; }

    public int Complexity { get; init; }

    public bool IncreasesComplexity { get; init; }

    public bool DeceasesComplexity { get; init; }

    public IReadOnlySet<IMessage> Variables { get; private set; }

    private void CollectVariables()
    {
        HashSet<IMessage> allVars = new();
        foreach (IMessage premise in Premises)
        {
            premise.CollectVariables(allVars);
        }
        Variables = allVars;
    }

    public bool ContainsMessage(IMessage msg)
    {
        return Result.ContainsMessage(msg) || (from p in Premises where p.ContainsMessage(msg) select p).Any();
    }

    public bool ContainsNonce
    {
        get
        {
            static bool nonceFinder(IMessage msg) => msg is NonceMessage;
            HashSet<IMessage> nonces = new();
            foreach (IMessage msg in Premises)
            {
                msg.CollectMessages(nonces, nonceFinder);
                if (nonces.Count > 0)
                {
                    return true;
                }
            }
            Result.CollectMessages(nonces, nonceFinder);
            return nonces.Count > 0;
        }
    }

    public bool IsKnownFrom(HashSet<IMessage> knowledge)
    {
        HashSet<IMessage> foundUnknowns = new();
        foreach (IMessage p in Premises)
        {
            if (p is not NonceMessage)
            {
                p.CollectMessages(foundUnknowns, (IMessage msg) => msg is NameMessage && !knowledge.Contains(msg));
                if (foundUnknowns.Count > 0)
                {
                    return false;
                }
            }
        }
        return true;
    }
    
    public bool IsResolved => Variables.Count == 0;

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is HornClause hc &&
            Result.Equals(hc.Result) &&
            Premises.Count == hc.Premises.Count &&
            Premises.SetEquals(hc.Premises) &&
            Rank == hc.Rank &&
            Guard.Equals(hc.Guard);
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

    public override string ToString() => Rank.ToString() + "#" + String.Join(", ", Premises) + " -> " + Result.ToString();

    #endregion
    #region Rule conversions.

    public static HornClause? FromStateConsistentRule(StateConsistentRule scr)
    {
        if (scr.Snapshots.IsEmpty)
        {
            return new(
                scr.Result.Messages.Single(), 
                from p in scr.Premises where p.IsKnow select p.Messages.Single(), 
                scr.GuardStatements);
        }
        return null;
    }

    public StateConsistentRule ToStateConsistentRule(string label = "")
    {
        HashSet<Event> premiseEvents = new(from p in Premises select Event.Know(p));
        return new(label, new(), premiseEvents, new(), Event.Know(Result));
    }

    #endregion
}

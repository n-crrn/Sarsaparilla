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
        Premises = new SortedSet<IMessage>(premises, MessageUtils.SortComparer);
        Result = result;

        int premiseComplexity = Premises.Count == 0 ? 0 : (from p in Premises select p.FindMaximumDepth()).Max();
        int resultComplexity = Result.FindMaximumDepth();
        Complexity = Math.Max(premiseComplexity, resultComplexity);
        IncreasesDepthBy = resultComplexity - premiseComplexity;

        CalculateHashCode();
        CollectVariables();
        CanBeSelfReferential = DetermineSelfReferential();
        Debug.Assert(Variables != null);
    }

    #region Properties.

    public Guard Guard { get; init; }

    public IReadOnlySet<IMessage> Premises { get; init; }

    public IMessage Result { get; init; }

    public bool ComplexResult => Result is FunctionMessage || Result is TupleMessage;

    /// <summary>
    /// This is a publicly setable boolean used by some algorithms that work with HornClauses, 
    /// such as the horn clause elaboration within the query engine.
    /// </summary>
    public bool Mark { get; set; }

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

    public int IncreasesDepthBy { get; init; }

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

    private bool DetermineSelfReferential()
    {
        if (Result is VariableMessage)
        {
            return false;
        }
        foreach (IMessage msg in Premises)
        {
            if (msg is not VariableMessage && Result.IsUnifiableWith(msg))
            {
                return true;
            }
        }
        return false;
    }

    public bool CanBeSelfReferential { get; private set; }

    #endregion
    #region Operations.

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

    public HornClause? ComposeUponAndScrub(HornClause other)
    {
        HornClause? composed = ComposeUpon(other);
        if (composed != null)
        {
            composed = composed.ScrubLooseVariables();
        }
        return composed;
    }

    public HornClause? ComposeUpon(HornClause other)
    {
        if ((!ComplexResult && Result is not NameMessage) || (Rank != other.Rank && Rank != -1 && other.Rank != -1))
        {
            return null;
        }

        HornClause lastPass = other;
        bool changed = true;
        bool substitutionDone = false;
        bool maySelfReference = CanBeSelfReferential;

        do
        {
            changed = false;
            foreach (IMessage msg in lastPass.Premises)
            {
                if (msg is not VariableMessage && msg.GetType() == Result.GetType())
                {
                    SigmaFactory sf = new();
                    if (Result.DetermineUnifiableSubstitution(msg, Guard, lastPass.Guard, sf))
                    {
                        SigmaMap fwdMap = sf.CreateForwardMap();
                        SigmaMap bwdMap = sf.CreateBackwardMap();

                        HornClause thisUpdated = Substitute(fwdMap);
                        IEnumerable<IMessage> oPremises = (from op in lastPass.Premises where !op.Equals(msg) select op.PerformSubstitution(bwdMap)).Concat(thisUpdated.Premises);
                        IMessage oResult = lastPass.Result.PerformSubstitution(bwdMap);
                        HornClause otherUpdated = new(oResult, oPremises)
                        {
                            Guard = Guard.PerformSubstitution(fwdMap).Union(lastPass.Guard.PerformSubstitution(bwdMap))
                        };

                        // Final check - ensure result not in premise.
                        if (!otherUpdated.Premises.Contains(otherUpdated.Result))
                        {
                            otherUpdated.Parent = this;
                            otherUpdated.Rank = RatchetRank(Rank, lastPass.Rank); // FIXME: Too simple.
                            otherUpdated.Source = new CompositionRuleSource(this, other);
                            //otherUpdated.Mark = true;
                            lastPass = otherUpdated;
                            changed = true;
                            break;
                        }
                    }
                }
            }
            substitutionDone |= changed;
            changed = changed && !maySelfReference; // Don't get stuck in an infinite loop.
        } while (changed);

        return substitutionDone ? lastPass : null;
    }

    public HornClause ScrubLooseVariables()
    {
        // Split the premises into variables and non-variables.
        List<IMessage> other = new();
        HashSet<IMessage> suspects = new();
        foreach (IMessage premise in Premises)
        {
            if (premise is VariableMessage)
            {
                suspects.Add(premise);
            }
            else
            {
                other.Add(premise);
            }
        }

        // Determine which variables are required for the rule.
        HashSet<IMessage> usefulVarSet = new();
        Result.CollectVariables(usefulVarSet);
        foreach (IMessage otherPremise in other)
        {
            otherPremise.CollectVariables(usefulVarSet);
        }
        foreach ((IAssignableMessage assigner, HashSet<IMessage> banned) in Guard.Ununified)
        {
            assigner.CollectVariables(usefulVarSet);
            foreach (IMessage b in banned)
            {
                b.CollectVariables(usefulVarSet);
            }
        }

        // Remove the useful variables from the suspects list.
        suspects.ExceptWith(usefulVarSet);

        // Remove the remaining culprits.
        if (suspects.Count == 0)
        {
            return this;
        }
        return new(Result, Premises.Except(suspects), Guard)
        {
            Rank = Rank,
            Source = new OperationRuleSource(this, OperationRuleSource.Op.Scrub)
        };
    }

    public bool Implies(HornClause hc)
    {
        if (hc.Premises.Count < Premises.Count)
        {
            return false;
        }

        SigmaFactory sf = new();
        if (Result.DetermineUnifiedToSubstitution(hc.Result, Guard, sf))
        {
            if (Premises.Count > 0)
            { 
                int lastOtherPremiseIndex = 0;
                List<IMessage> thisPremises = Premises.ToList();
                List<IMessage> otherPremises = hc.Premises.ToList();
                for (int i = 0; i < Premises.Count; i++)
                {
                    bool found = false;
                    SigmaFactory? nextSf = null;
                    for (; lastOtherPremiseIndex < hc.Premises.Count; lastOtherPremiseIndex++)
                    {
                        nextSf = new(sf);
                        IMessage otherMsg = otherPremises[lastOtherPremiseIndex];
                        if (thisPremises[i].DetermineUnifiedToSubstitution(otherMsg, Guard, nextSf))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        return false;
                    }
                    sf = nextSf!;
                }
                return Rank == hc.Rank && Guard.Equals(hc.Guard);
            }
            else
            {
                return Rank <= hc.Rank && Guard.Equals(hc.Guard);
            }
        }

        return false;
    }

    public bool CanResultIn(IMessage possResult, Guard bwdGuard, out SigmaFactory? sf)
    {
        sf = new();
        if (Result.DetermineUnifiableSubstitution(possResult, Guard, bwdGuard, sf))
        {
            HashSet<IMessage> resultVars = new();
            Result.CollectVariables(resultVars);
            HashSet<IMessage> danglingVars = new(sf.CreateForwardMap().InsertedVariables);
            danglingVars.ExceptWith(resultVars);
            danglingVars.IntersectWith(Variables);
            if (danglingVars.Count > 0)
            {
                SigmaMap moveMap = new(from dv in danglingVars 
                                       select (dv, MessageUtils.SubscriptVariableMessage(dv, "i")));
                sf.ForwardSubstitute(moveMap);
            }
            return true;
        }
        return false;
    }

    #endregion
    #region Collection operations.

    /// <summary>
    /// Removes rules that result in a constant value but have no premises.
    /// </summary>
    /// <param name="clauses">List of clauses to filter.</param>
    /// <returns>
    /// The provided list itself. This return is meant as a convenience.
    /// </returns>
    public static List<HornClause> FilterImpliedRules(List<HornClause> clauses)
    {
        for (int i = 0; i < clauses.Count; i++)
        {
            HornClause h1 = clauses[i];

            for (int j = 0; j < clauses.Count; j++)
            {
                if (i != j)
                {
                    HornClause h2 = clauses[j];
                    //if (h1.EqualsIgnoringRank(h2))
                    if (h1.Implies(h2))
                    {
                        if (h1.Rank >= h2.Rank)
                        {
                            clauses.RemoveAt(i);
                            i--;
                            break;
                        }
                        else
                        {
                            clauses.RemoveAt(j);
                            j--;
                            if (i > j)
                            {
                                i--;
                                break;
                            }
                        }
                    }
                }
            }
        }
        return clauses;
    }

    /// <summary>
    /// This will compose the HornClauses in appliers to thisSet continually until a minimal set of
    /// rules is established.
    /// </summary>
    /// <param name="appliers">Horn Clauses to be applied to reduce the set.</param>
    /// <param name="thisSet">The set of Horn Clauses to be reduced.</param>
    /// <returns>The reduced set.</returns>
    public static HashSet<HornClause> FullyElaborateCollection(List<HornClause> appliers, HashSet<HornClause> thisSet)
    {
        bool changed = false;
        HashSet<HornClause> finalSet = new();
        do
        {
            changed = false;
            List<HornClause> nextSet = new();
            foreach (HornClause appliedTo in thisSet)
            {
                List<HornClause> processed1 = new() { appliedTo };
                List<HornClause> processed2 = new();
                foreach (HornClause cr in appliers)
                {
                    foreach (HornClause p in processed1)
                    {
                        HornClause? composed = cr.ComposeUponAndScrub(p);
                        if (composed != null)
                        {
                            processed2.Add(composed);
                            cr.Mark = true;
                            p.Mark = true;
                            changed = true;
                        }
                    }
                    processed2.AddRange(processed1);
                    processed1.Clear();
                    (processed1, processed2) = (processed2, processed1);
                }
                nextSet.AddRange(from p in processed1 where !p.Mark select p);
            }

            appliers.RemoveAll((HornClause hc) => !hc.Mark || hc.CanBeSelfReferential);
            foreach (HornClause a in appliers)
            {
                a.Mark = false;
            }

            thisSet = new(FilterImpliedRules(nextSet.ToList()));
        } while (changed);

        return thisSet;
    }

    /// <summary>
    /// Does a naive elaboration - that is, splits out the rules of increasing complexity and tries
    /// to compose them upon the remainder. The effectiveness of this method is not guaranteed, but
    /// is tied to the nature of the ruleset itself. Therefore, this method is provided for
    /// academic study only, and is not recommended for use.
    /// </summary>
    /// <param name="fullRuleset">The ruleset to composed.</param>
    /// <returns>The composed ruleset.</returns>
    public static HashSet<HornClause> NaiveElaborate(HashSet<HornClause> fullRuleset)
    {
        // Determine which rules are "complex" ones - that is, result in something complex.
        fullRuleset = new(FilterImpliedRules(fullRuleset.ToList()));
        List<HornClause> complexResults = new(fullRuleset.Count);
        HashSet<HornClause> simpleResults = new(fullRuleset.Count);
        foreach (HornClause hc in fullRuleset)
        {
            if ((hc.ComplexResult && hc.IncreasesDepthBy > 0) || hc.Result is NameMessage)
            {
                complexResults.Add(hc);
            }
            else
            {
                simpleResults.Add(hc);
            }
        }

        // Now apply the complex rules to the "simple" ones.
        HashSet<HornClause> finalSet = HornClause.FullyElaborateCollection(new(complexResults), simpleResults);
        HashSet<HornClause> finishedRuleset = new(complexResults.Concat(finalSet));
        return finishedRuleset;
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is HornClause hc &&
            Rank == hc.Rank &&
            EqualsIgnoringRank(hc);
    }

    public bool EqualsIgnoringRank(HornClause hc)
    {
        return Premises.Count == hc.Premises.Count &&
            Result.Equals(hc.Result) &&
            Premises.SetEquals(hc.Premises) &&
            Guard.Equals(hc.Guard);
    }

    private int Hash;

    private void CalculateHashCode()
    {
        Hash = Result.GetHashCode();
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

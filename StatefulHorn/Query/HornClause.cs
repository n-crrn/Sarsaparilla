using System;
using System.Collections.Generic;
using System.Linq;
using StatefulHorn.Messages;
using StatefulHorn.Query.Origin;

namespace StatefulHorn.Query;

/// <summary>
/// The implementation of a Ranked Horn Clause. A horn clause is a structure that has a set of 
/// premises that lead to a conclusion. A Ranked Horn Clause has, in addition, a integer value
/// called "rank" that determines an order of precedence that the clauses provide premises
/// based on results. Guard information is also attached.
/// </summary>
public class HornClause
{
    /// <summary>Create a new Horn Clause.</summary>
    /// <param name="result">
    /// Resulting message from the clause. The variables within the result must appear in one or
    /// more 
    /// </param>
    /// <param name="premises">The premises leading to the result.</param>
    /// <param name="guard">Restrictions on variables.</param>
    public HornClause(IMessage result, IEnumerable<IMessage> premises, Guard? guard = null)
    {
        Premises = new SortedSet<IMessage>(premises, MessageUtils.SortComparer);
        Result = result;
        HashCode = result.GetHashCode();
        Variables = CollectVariables();
        Guard = guard == null ? Guard.Empty : guard.Filter(Variables);
    }

    /// <summary>
    /// Collect all variable messages used within the clause, and add them to the one set.
    /// </summary>
    /// <returns>Set of all variable messages.</returns>
    private HashSet<IMessage> CollectVariables()
    {
        HashSet<IMessage> allVars = new();
        foreach (IMessage premise in Premises)
        {
            premise.CollectVariables(allVars);
        }
        return allVars;
    }

    /// <summary>
    /// Determine if the result of this clause is referential with itself. This is used to 
    /// det
    /// </summary>
    /// <returns></returns>
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

    #region Properties.

    /// <summary>
    /// Guard statement indicating which variable substitutions cannot be conducted with this
    /// clause.
    /// </summary>
    public Guard Guard { get; private init; }

    /// <summary>Premises for the Clause.</summary>
    public IReadOnlySet<IMessage> Premises { get; private init; }

    /// <summary>Result message of the Clause.</summary>
    public IMessage Result { get; private init; }

    /// <summary>
    /// Indicates that the Horn Clause will result in a non-atomic message. Used when there is
    /// a need to restrict the combinations of compositions and elaborations of clauses.
    /// </summary>
    public bool ComplexResult => Result is FunctionMessage || Result is TupleMessage;

    /// <summary>
    /// This is a publicly setable boolean used by some algorithms that work with HornClauses
    /// without requiring some sort of external map.
    /// </summary>
    public bool Mark { get; set; }

    /// <summary>
    /// The value of rank used for a Horn Clause that can be applied at any point in a system.
    /// </summary>
    public const int InfiniteRank = -1;

    /// <summary>
    /// An integer indicating the ordering of this Horn Clause. 
    /// </summary>
    public int Rank { get; set; } = InfiniteRank;

    /// <summary>
    /// Indicates that this rule can be treated as occurring before the given rank by the
    /// rules of the rank system.
    /// </summary>
    /// <param name="r">Rank to compare with.</param>
    /// <returns>True if this rule should be treated as occurring before rank r.</returns>
    public bool BeforeRank(int r) => Rank == -1 || r == -1 || Rank <= r;

    /// <summary>
    /// When two rules are combined, or a result is derived from a combination of clauses, this
    /// method returns the new rank of the result or rule. The ordering of rank values provided
    /// does not matter.
    /// </summary>
    /// <param name="r1">First rank value.</param>
    /// <param name="r2">Second rank value.</param>
    /// <returns>The combined rank.</returns>
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

    /// <summary>
    /// Cache for IncreasesDepthBy.
    /// </summary>
    private int _IncreasesDepthBy = int.MinValue;

    /// <summary>
    /// The maximum difference of nesting count between the result of the Horn Clause and its
    /// premises.
    /// </summary>
    public int IncreasesDepthBy
    {
        get
        {
            if (_IncreasesDepthBy == int.MinValue)
            {
                int premiseComplexity = Premises.Count == 0 ? 0 : (from p in Premises select p.FindMaximumDepth()).Max();
                int resultComplexity = Result.FindMaximumDepth();
                _IncreasesDepthBy = resultComplexity - premiseComplexity;
            }
            return _IncreasesDepthBy;
        }
    }

    /// <summary>
    /// Set of variable messages used within the Horn Clause.
    /// </summary>
    public IReadOnlySet<IMessage> Variables { get; init; }

    /// <summary>
    /// Cache for the hash code calculation.
    /// </summary>
    private readonly int HashCode;

    #endregion
    #region Operations.

    /// <summary>
    /// Substitute messages within the Horn Clause as described in the provided Sigma Map.
    /// </summary>
    /// <param name="map">Substitutions to make.</param>
    /// <returns>
    /// A Horn Clause with the substitutions made. This Horn Clause is not modified, but may be
    /// returned if the Sigma Map is empty.
    /// </returns>
    public HornClause Substitute(SigmaMap map)
    {
        if (map.IsEmpty)
        {
            return this;
        }

        IMessage updatedResult = Result.Substitute(map);
        HornClause hc = new(updatedResult, from p in Premises select p.Substitute(map))
        {
            Rank = Rank,
            Source = new SubstitutionRuleSource(this, map),
            Guard = Guard.Substitute(map)
        };
        return hc;
    }

    /// <summary>
    /// If this Horn Clause results in a tuple, then it will be split into a series of Horn Clauses
    /// that result in the individual components of the tuple. Otherwise, the Horn Clause is
    /// returned as is.
    /// </summary>
    /// <returns>
    /// A unordered sequence of Horn Clauses leading to the components of the result.
    /// </returns>
    public IEnumerable<HornClause> DetupleResult()
    {
        if (Result is TupleMessage tMsg)
        {
            foreach (IMessage member in tMsg.Members)
            {
                HornClause innerHC = new(member, Premises)
                {
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

    /// <summary>
    /// Compose this Horn Clause upon another where the other has a premise that is unifiable with
    /// the result of this one. This method will also remove any plain variable premises that are
    /// not reflected in the result.
    /// </summary>
    /// <param name="other">Horn Clause to compose upon.</param>
    /// <returns>
    /// Null if this Horn Clause could not be composed upon other. Otherwise, a new Horn Clause is
    /// returned reflecting the composition and scrubbed of loose variables.
    /// </returns>
    public HornClause? ComposeUponAndScrub(HornClause other)
    {
        HornClause? composed = ComposeUpon(other);
        if (composed != null)
        {
            composed = composed.ScrubLooseVariables();
        }
        return composed;
    }

    /// <summary>
    /// Compose this Horn Clause upon another where the other has a prmeise that is unifiable with
    /// the result of this one.
    /// </summary>
    /// <param name="other">Horn Clause to compose upon.</param>
    /// <returns>
    /// Null if the result of this clause cannot be unified to a premise of other. Otherwise, a new
    /// Horn Clause is returned describing the new composed clause.
    /// </returns>
    public HornClause? ComposeUpon(HornClause other)
    {
        if (!ComplexResult && Result is not NameMessage || Rank != other.Rank && Rank != -1 && other.Rank != -1)
        {
            return null;
        }

        HornClause lastPass = other;
        bool changed = true;
        bool substitutionDone = false;
        bool maySelfReference = DetermineSelfReferential();

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
                        IEnumerable<IMessage> oPremises = (from op in lastPass.Premises where !op.Equals(msg) select op.Substitute(bwdMap)).Concat(thisUpdated.Premises);
                        IMessage oResult = lastPass.Result.Substitute(bwdMap);
                        HornClause otherUpdated = new(oResult, oPremises)
                        {
                            Guard = Guard.Substitute(fwdMap).Union(lastPass.Guard.Substitute(bwdMap))
                        };

                        // Final check - ensure result not in premise.
                        if (!otherUpdated.Premises.Contains(otherUpdated.Result))
                        {
                            otherUpdated.Rank = RatchetRank(Rank, lastPass.Rank); // FIXME: Too simple.
                            otherUpdated.Source = new CompositionRuleSource(this, other);
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

    /// <summary>
    /// Remove loose variables - that is, premises that are composed of a single variable that
    /// do not appear in the result. As these premises can be simply replaced with constants
    /// such as 'true[]' that are known by any attacker, there is no value retaining them with
    /// the rule.
    /// </summary>
    /// <returns>
    /// This Horn Clause if there are no loose variables. Otherwise, a new Horn Clause with the
    /// loose/dangling variable premises removed.
    /// </returns>
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
        Guard.CollectVariables(usefulVarSet);

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

    /// <summary>
    /// Determines if this Horn Clause can be used as a more general - or at worst equivalent - 
    /// version of the given Horn Clause. For instance, the clause "f(a), b[] -> g(a, b[])" implies 
    /// "f(d[]), b[] -> g(d[], b[])". Rank is considered in determining this, but the Guards will
    /// only pass if they are equivalent.
    /// </summary>
    /// <param name="hc">Horn Clause to check.</param>
    /// <returns>True if this Horn Clause can imply hc.</returns>
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

    /// <summary>
    /// Determine if this Horn Clause can have substitutions applied that result in deriving the
    /// message possResult while complying with the guard bwdGuard. In effect, this is checking
    /// the unifiability of the results while accounting for possible dangling variables.
    /// </summary>
    /// <param name="possResult">Result to test unifiability with.</param>
    /// <param name="bwdGuard">Guard protecting possResult.</param>
    /// <param name="sf">
    /// Storage of the subtitutions required to make this Horn Clause match the requested result.
    /// If no match is possible, it is set to null.
    /// </param>
    /// <returns>True if this Horn Clause can result in possResult.</returns>
    public bool CanResultIn(IMessage possResult, Guard bwdGuard, out SigmaFactory? sf)
    {
        sf = new();
        if (Result.DetermineUnifiableSubstitution(possResult, Guard, bwdGuard, sf) 
            && sf.ForwardIsValidByGuard(Guard) 
            && sf.BackwardIsValidByGuard(bwdGuard))
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
        sf = null;
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

            appliers.RemoveAll((hc) => !hc.Mark || hc.DetermineSelfReferential());
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
            if (hc.ComplexResult && hc.IncreasesDepthBy > 0 || hc.Result is NameMessage)
            {
                complexResults.Add(hc);
            }
            else
            {
                simpleResults.Add(hc);
            }
        }

        // Now apply the complex rules to the "simple" ones.
        HashSet<HornClause> finalSet = FullyElaborateCollection(new(complexResults), simpleResults);
        HashSet<HornClause> finishedRuleset = new(complexResults.Concat(finalSet));
        return finishedRuleset;
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is HornClause hc &&
            Rank == hc.Rank &&
            Premises.Count == hc.Premises.Count &&
            Result.Equals(hc.Result) &&
            Premises.SetEquals(hc.Premises) &&
            Guard.Equals(hc.Guard);
    }

    public override int GetHashCode() => HashCode;

    public override string ToString() => Rank.ToString() + "#" + string.Join(", ", Premises) + " -> " + Result.ToString();

    #endregion
    #region Rule conversions.

    /// <summary>
    /// Attempt to return a Horn Clause version of the given State Consistent Rule. This will
    /// succeed if the rule does not have any state specifications.
    /// </summary>
    /// <param name="scr">State Consistent Rule to translate.</param>
    /// <returns>
    /// Null if the given State Consistent Rule is reliant on state. Otherwise, the Horn 
    /// Clause equivalent rule is returned.
    /// </returns>
    public static HornClause? FromStateConsistentRule(StateConsistentRule scr)
    {
        if (scr.Snapshots.IsEmpty)
        {
            return new(
                scr.Result.Messages.Single(),
                from p in scr.Premises where p.IsKnow select p.Messages.Single(),
                scr.Guard);
        }
        return null;
    }

    /// <summary>
    /// Create a stateless State Consistent Rule that is the equivalent of this Horn Clause.
    /// </summary>
    /// <param name="label">User understandable description of the new rule.</param>
    /// <returns>The Horn Clause as a State Consistent Rule.</returns>
    public StateConsistentRule ToStateConsistentRule(string label = "")
    {
        HashSet<Event> premiseEvents = new(from p in Premises select Event.Know(p));
        return new(label, Guard.Empty, premiseEvents, new(), Event.Know(Result));
    }

    #endregion
}

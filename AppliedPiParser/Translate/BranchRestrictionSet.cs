using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;
using AppliedPi.Model;
using AppliedPi.Model.Comparison;

namespace AppliedPi.Translate;

/// <summary>
/// Represents a set of other-wise exclusionary sets of value restrictions placed upon a branch.
/// </summary>
public class BranchRestrictionSet
{
    private BranchRestrictionSet(List<IfBranchConditions> ifCond, List<IfBranchConditions> elseCond)
    {
        IfConditions = ifCond;
        ElseConditions = elseCond;
    }

    private BranchRestrictionSet((IfBranchConditions, IfBranchConditions) singleCmpConditions)
        : this(new() { singleCmpConditions.Item1 }, new() { singleCmpConditions.Item2 })
    { }

    public static BranchRestrictionSet From(IComparison comp, ResolvedNetwork rn, Network nw) => FromRaw(comp.Positivise(), rn, nw);

    private static BranchRestrictionSet FromRaw(IComparison comp, ResolvedNetwork rn, Network nw)
    {
        return comp switch
        {
            IsComparison ic => new(FromIsComparison(ic, rn, nw)),
            NotComparison nc => new(FromNotComparison(nc, rn, nw)),
            BooleanComparison bc => FromBooleanComparison(bc, rn, nw),
            EqualityComparison ec => FromEqualityComparison(ec, rn, nw),
            _ => throw new NotImplementedException($"Unrecognised comparison type {comp.GetType()}"),
        };
    }

    public List<(SigmaMap, Guard)> OutputIfBranchConditions() => ConvertBranchConditions(IfConditions);

    public List<(SigmaMap, Guard)> OutputElseBranchConditions() => ConvertBranchConditions(ElseConditions);

    private static List<(SigmaMap, Guard)> ConvertBranchConditions(List<IfBranchConditions> ibc)
    {
        List<(SigmaMap, Guard)> all = new();
        foreach (IfBranchConditions c in ibc)
        {
            all.Add((c.CreateSigmaMap(), c.CreateGuard()));
        }
        return all;
    }

    #region Condition updating and manipulation.

    private readonly List<IfBranchConditions> IfConditions;
    private readonly List<IfBranchConditions> ElseConditions;

    public BranchRestrictionSet Not() => new(ElseConditions, IfConditions);

    public BranchRestrictionSet And(BranchRestrictionSet brs)
    {
        List<IfBranchConditions> ifCond = new();
        foreach (IfBranchConditions c1 in IfConditions)
        {
            foreach (IfBranchConditions c2 in brs.IfConditions)
            {
                IfBranchConditions newCond = new(c1);
                newCond.AndWith(c2);
                ifCond.Add(newCond);
            }
        }
        List<IfBranchConditions> elseCond = new(ElseConditions.Concat(brs.ElseConditions));
        return new(ifCond, elseCond);
    }

    public BranchRestrictionSet Or(BranchRestrictionSet brs)
    {
        List<IfBranchConditions> ifCond = new(IfConditions.Concat(brs.IfConditions));
        List<IfBranchConditions> elseCond = new();
        foreach (IfBranchConditions e1 in ElseConditions)
        {
            foreach (IfBranchConditions e2 in brs.ElseConditions)
            {
                IfBranchConditions newElseCond = new(e1);
                newElseCond.AndWith(e2);
                elseCond.Add(newElseCond);
            }
        }
        return new(ifCond, elseCond);
    }

    #endregion
    #region Direct ICondition processing.

    private static readonly IMessage TrueMessage = new NameMessage("true");

    // private static readonly IMessage FalseMessage = new NameMessage("false");

    public record EqualityCondition(IMessage Result, IDictionary<IMessage, IMessage> Correspondences);

    private static GroupSet<IMessage> GetEqualityCorrespondences(
        IMessage lhsMsg, 
        IMessage rhsMsg, 
        IReadOnlySet<Destructor> destructors)
    {
        if (lhsMsg is VariableMessage)
        {
            return new GroupSet<IMessage>(lhsMsg, rhsMsg);
        }
        else if (rhsMsg is VariableMessage)
        {
            return new GroupSet<IMessage>(rhsMsg, lhsMsg);
        }
        else if (lhsMsg is NameMessage nLhsMsg)
        {
            if (rhsMsg is FunctionMessage fRhsMsg)
            {
                return NameCorrespondence(nLhsMsg, fRhsMsg, destructors);
            }
            throw new InvalidComparisonException(lhsMsg, rhsMsg, "Terms not comparable.");
        }
        else if (rhsMsg is NameMessage nRhsMsg)
        {
            if (lhsMsg is FunctionMessage fLhsMsg)
            {
                return NameCorrespondence(nRhsMsg, fLhsMsg, destructors);
            }
            throw new InvalidComparisonException(lhsMsg, rhsMsg, "Terms not comparable.");
        }
        else if (lhsMsg is TupleMessage t1Msg && rhsMsg is TupleMessage t2Msg)
        {
            return TupleToTupleCorrespondence(t1Msg, t2Msg, destructors);
        }
        else if (lhsMsg is FunctionMessage f1Msg && rhsMsg is FunctionMessage f2Msg)
        {
            return FunctionToFunctionCorrespondence(f1Msg, f2Msg, destructors);
        }
        else
        {
            TupleMessage tMsg;
            FunctionMessage fMsg;
            if (lhsMsg is TupleMessage tLhsMsg)
            {
                tMsg = tLhsMsg;
                fMsg = (FunctionMessage)rhsMsg;
            }
            else
            {
                tMsg = (TupleMessage)rhsMsg;
                fMsg = (FunctionMessage)lhsMsg;
            }
            return TupleToFunctionCorrespondence(tMsg, fMsg, destructors);
        }
    }

    private static GroupSet<IMessage> NameCorrespondence(
        NameMessage nMsg, 
        FunctionMessage fMsg, 
        IReadOnlySet<Destructor> destructors)
    {
        Destructor dest = FindDestructor(destructors, fMsg);
        BucketSet<Term, IMessage> corres = GetTermCorrespondences(fMsg, dest.LeftHandSide);
        corres.Add(new(dest.RightHandSide), nMsg);
        GroupSet<IMessage> eqGroups = new();
        FromTermsToEqualityGroups(corres, eqGroups);
        return eqGroups;
    }

    private static GroupSet<IMessage> TupleToTupleCorrespondence(
        TupleMessage t1Msg, 
        TupleMessage t2Msg, 
        IReadOnlySet<Destructor> destructors)
    {
        if (t1Msg.Members.Count != t2Msg.Members.Count)
        {
            throw new InvalidComparisonException(t1Msg, t2Msg, "Constant expression.");
        }
        GroupSet<IMessage> fullEqCorres = new();
        for (int i = 0; i < t1Msg.Members.Count; i++)
        {
            GroupSet<IMessage> memberCorres = GetEqualityCorrespondences(t1Msg.Members[i], t2Msg.Members[i], destructors);
            fullEqCorres.UnionWith(memberCorres);
        }
        return fullEqCorres;
    }

    private static GroupSet<IMessage> TupleToFunctionCorrespondence(
        TupleMessage tMsg, 
        FunctionMessage fMsg, 
        IReadOnlySet<Destructor> destructors)
    {
        Destructor dest = FindDestructor(destructors, fMsg);
        BucketSet<Term, IMessage> corres = GetTermCorrespondences(fMsg, dest.LeftHandSide);
        Term rhs = dest.RightHandTerm;
        if (rhs.Parameters.Count == tMsg.Members.Count)
        {
            for (int i = 0; i < rhs.Parameters.Count; i++)
            {
                corres.Add(rhs.Parameters[i], tMsg.Members[i]);
            }
        }
        else if (rhs.Parameters.Count == 0)
        {
            corres.Add(rhs, tMsg);
        }
        else
        {
            throw new InvalidComparisonException(fMsg, rhs);
        }
        GroupSet<IMessage> fullEqCorres = new();
        FromTermsToEqualityGroups(corres, fullEqCorres);
        return fullEqCorres;
    }

    private static GroupSet<IMessage> FunctionToFunctionCorrespondence(
        FunctionMessage f1Msg, 
        FunctionMessage f2Msg, 
        IReadOnlySet<Destructor> destructors)
    {
        Destructor dest1 = FindDestructor(destructors, f1Msg);
        Destructor dest2 = FindDestructor(destructors, f2Msg);
        Term dest1Rhs = dest1.RightHandTerm;
        Term dest2Rhs = dest2.RightHandTerm;
        BucketSet<Term, IMessage> corres1 = GetTermCorrespondences(f1Msg, dest1.LeftHandSide);
        BucketSet<Term, IMessage> corres2 = GetTermCorrespondences(f2Msg, dest2.LeftHandSide);

        // Connect a siphon between the buckets for the GroupSets to pick up on.
        if (dest1Rhs.Parameters.Count == dest2Rhs.Parameters.Count)
        {
            IMessage resultCorres1 = corres1[dest1Rhs].First();
            IMessage resultCorres2 = corres2[dest2Rhs].First();
            corres2.Add(dest2Rhs, resultCorres1);
            corres1.Add(dest1Rhs, resultCorres2);
        }
        else if (dest1Rhs.Parameters.Count == 0)
        {
            IEnumerable<IMessage> tuple2SampleValues = from r in dest2Rhs.Parameters select corres2[r].First();
            corres1.Add(dest1Rhs, new TupleMessage(tuple2SampleValues));
        }
        else if (dest2Rhs.Parameters.Count == 0)
        {
            IEnumerable<IMessage> tuple1SampleValues = from r in dest1Rhs.Parameters select corres1[r].First();
            corres2.Add(dest2Rhs, new TupleMessage(tuple1SampleValues));
        }
        else
        {
            throw new InvalidComparisonException(dest1Rhs, dest2Rhs, "Tuple member count mismatch.");
        }

        GroupSet<IMessage> eqCorres = new();
        FromTermsToEqualityGroups(corres1, eqCorres);
        FromTermsToEqualityGroups(corres2, eqCorres);
        return eqCorres;
    }

    private static Destructor FindDestructor(IReadOnlySet<Destructor> destructors, FunctionMessage fMsg)
    {
        // This method exists in case we need to implement multiple destructors for every function.
        foreach (Destructor d in destructors)
        {
            if (d.LeftHandSide.Name == fMsg.Name)
            {
                return d;
            }
        }
        throw new InvalidComparisonException(fMsg, "No destructor exists in comparison with name.");
    }

    private static void FromTermsToEqualityGroups(BucketSet<Term, IMessage> original, GroupSet<IMessage> collection)
    {
        foreach ((Term _, IReadOnlySet<IMessage> eqlMsgSet) in original)
        {
            IEnumerator<IMessage> msgList = eqlMsgSet.GetEnumerator();
            msgList.MoveNext();
            IMessage first = msgList.Current; // There will be at least one item.
            collection.Add(first);
            while (msgList.MoveNext())
            {
                collection.Add(first, msgList.Current);
            }
        }
    }

    private static BucketSet<Term, IMessage> GetTermCorrespondences(IMessage msg, Term destructorTerm)
    {
        BucketSet<Term, IMessage> corres = new();
        switch (msg)
        {
            case FunctionMessage fMsg:
                if (destructorTerm.Parameters.Count == 0)
                {
                    corres.Add(destructorTerm, fMsg);
                }
                else if (destructorTerm.Parameters.Count == fMsg.Parameters.Count && destructorTerm.Name == fMsg.Name)
                {
                    for (int i = 0; i < destructorTerm.Parameters.Count; i++)
                    {
                        corres.UnionWith(GetTermCorrespondences(fMsg.Parameters[i], destructorTerm.Parameters[i]));
                    }
                }
                else
                {
                    throw new InvalidComparisonException(fMsg, destructorTerm);
                }
                break;
            case TupleMessage tMsg:
                if (destructorTerm.Parameters.Count == 0)
                {
                    corres.Add(destructorTerm, tMsg);
                }
                else if (destructorTerm.Parameters.Count == tMsg.Members.Count)
                {
                    for (int i = 0; i < destructorTerm.Parameters.Count; i++)
                    {
                        corres.UnionWith(GetTermCorrespondences(tMsg.Members[i], destructorTerm.Parameters[i]));
                    }
                }
                else
                {
                    throw new InvalidComparisonException(tMsg, destructorTerm);
                }
                break;
            case NameMessage nMsg:
                corres.Add(destructorTerm, nMsg);
                break;
            case VariableMessage vMsg:
                corres.Add(destructorTerm, vMsg);
                break;
            default:
                throw new NotImplementedException();
        }
        return corres;
    }

    private static (IfBranchConditions, IfBranchConditions) FromIsComparison(IsComparison ic, ResolvedNetwork rn, Network nw)
    {
        // Note that this particular comparison method should not be called in
        // any case that it is explicitly boolean. In that case, the test is
        // "msg == true".
        Term t = ic.AsTerm;
        if (!rn.CheckTermType(t, PiType.Bool))
        {
            throw new InvalidComparisonException(t, PiType.Bool);
        }
        IMessage msg = rn.TermToMessage(t);
        GroupSet<IMessage> eqCorres = GetEqualityCorrespondences(TrueMessage, msg, nw.Destructors);
        return (IfBranchConditions.IfThen(eqCorres), IfBranchConditions.Else(eqCorres));
    }

    private static (IfBranchConditions, IfBranchConditions) FromNotComparison(NotComparison nc, ResolvedNetwork rn, Network nw)
    {
        // Note that this particular comparison method should not be called in
        // any case other than that the comparison encapsulated by the 
        // NotComparison nc is an IsComparison.
        if (nc.InnerComparison is IsComparison ic)
        {
            (IfBranchConditions elseCond, IfBranchConditions ifCond) = FromIsComparison(ic, rn, nw);
            return (ifCond, elseCond);
        }
        throw new InvalidComparisonException($"Not expression contained {nc.InnerComparison.GetType()} instead of IsComparison.");
    }

    private static BranchRestrictionSet FromEqualityComparison(EqualityComparison ec, ResolvedNetwork rn, Network nw)
    {
        // If this is a general comparison then both ec.LeftComparison and ec.RightComparison are
        // IsComparisons. We can reach in and use GetEqualityCorrespondences. 
        if (ec.LeftComparison is IsComparison leftIs && ec.RightComparison is IsComparison rightIs)
        {
            IMessage leftMsg = rn.TermToMessage(leftIs.AsTerm);
            IMessage rightMsg = rn.TermToMessage(rightIs.AsTerm);
            GroupSet<IMessage> eqCorres = GetEqualityCorrespondences(leftMsg, rightMsg, nw.Destructors);
            IfBranchConditions ifCond = IfBranchConditions.IfThen(eqCorres);
            IfBranchConditions elseCond = IfBranchConditions.Else(eqCorres);
            if (!ec.IsEquals)
            {
                (elseCond, ifCond) = (ifCond, elseCond);
            }
            return new((ifCond, elseCond));
        }

        // Otherwise, it is a boolean comparison, and boolean style operations will be required.
        BranchRestrictionSet leftSet = FromRaw(ec.LeftComparison, rn, nw);
        BranchRestrictionSet rightSet = FromRaw(ec.RightComparison, rn, nw);
        if (ec.IsEquals)
        {
            return leftSet.And(rightSet);
        }
        return leftSet.And(rightSet.Not()).Or(leftSet.Not().And(rightSet));
    }

    private static BranchRestrictionSet FromBooleanComparison(BooleanComparison bc, ResolvedNetwork rn, Network nw)
    {
        BranchRestrictionSet leftSet = FromRaw(bc.LeftInput, rn, nw);
        BranchRestrictionSet rightSet = FromRaw(bc.RightInput, rn, nw);
        return bc.Operator == BooleanComparison.Type.And ? leftSet.And(rightSet) : leftSet.Or(rightSet);
    }

    #endregion
}

public class InvalidComparison : Exception
{
    public InvalidComparison(IsComparison item) : base($"Comparison term '{item}' is not a boolean.") { }
}


using System;
using System.Collections.Generic;
using System.Linq;

using AppliedPi.Model;
using AppliedPi.Processes;
using AppliedPi.Translate.MutateRules;
using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate;

/// <summary>
/// This does not manipulate the sockets or the values retrieved from them. However, the setting
/// of a value does depend on which branch the model's logic passes through, and it is possible
/// that in relying entirely on premises that the premises may be fulfilled (by accident) through
/// another branch.
/// </summary>
public class LetValueSetFactory 
{

    public LetValueSetFactory(
        LetProcess lp, 
        ResolvedNetwork rn, 
        Network nw, 
        IEnumerable<Socket> previousSockets, 
        IEnumerable<Socket> nextSockets,
        HashSet<Event> premises)
    {
        Let = lp;
        ResolvedNetwork = rn;
        Network = nw;
        PreviousSockets = new List<Socket>(previousSockets);
        NextSockets = new List<Socket>(from ns in nextSockets where !ns.IsInfinite select ns);
        Premises = new HashSet<Event>(premises);
    }

    private LetProcess Let { get; init; }

    private ResolvedNetwork ResolvedNetwork { get; init; }

    private Network Network { get; init; }

    public IReadOnlySet<Event> Premises { get; init; }

    public IReadOnlyList<Socket> PreviousSockets { get; init; }

    public IReadOnlyList<Socket> NextSockets { get; init; }

    /// <summary>
    /// This is the specific premise, which can be used in the guarded branch of the let process.
    /// </summary>
    public IMessage StoragePremiseMessage
    {
        get
        {
            List<IMessage> msgParameters = new();
            foreach (TuplePattern.Element ele in Let.LeftHandSide.Elements)
            {
                if (ele.IsMatcher)
                {
                    msgParameters.Add(ResolvedNetwork.TermToMessage(ele.Term));
                }
                else
                {
                    msgParameters.Add(new VariableMessage(ele.Term.Name));
                }
            }
            if (msgParameters.Count == 1)
            {
                return new FunctionMessage(CellName, msgParameters);
            }
            return new FunctionMessage(CellName, new() { new TupleMessage(msgParameters) });
        }
    }

    /// <summary>
    /// This is the general premise, which can be used in the else branch of the let process.
    /// </summary>
    public IMessage EmptyStoragePremiseMessage
    {
        get
        {
            return new FunctionMessage(CellName, new() { new VariableMessage(UniqueDesignation) });
        }
    }

    private string UniqueDesignation
    {
        get
        {
            List<string> cellNameParts = new();
            foreach (TuplePattern.Element ele in Let.LeftHandSide.Elements)
            {
                if (!ele.IsMatcher)
                {
                    cellNameParts.Add(ele.Term.Name);
                }
            }
            if (cellNameParts.Count == 0)
            {
                // The parser should have ensured that there was at least one assignable variable
                // included. Something has gone wrong.
                throw new InvalidOperationException($"Invalid let {Let} provided for conversion.");
            }
            return string.Join('@', cellNameParts);
        }
    }

    private string CellName => UniqueDesignation + "@cell";

    public VariableMessage Variable => new(UniqueDesignation);

    public IEnumerable<MutateRule> GenerateSetRules() => InnerGenerateSetRules(Let.RightHandSide, IfBranchConditions.Empty);

    private static int dId = 0;

    private IEnumerable<MutateRule> InnerGenerateSetRules(ITermGenerator iGen, IfBranchConditions branchCond)
    {
        string uniqueDesig = UniqueDesignation;

        if (iGen is Term t)
        {
            IMessage termAsMsg = ResolvedNetwork.TermToMessage(t);
            
            if (termAsMsg is FunctionMessage fMsg)
            {
                // For every destructor, an explicit rule must be included to effect the
                // translation. This is because the rules DO NOT indicate equivalence. 
                // They indicate knowledge following from. Being enclosed within the 
                // cell tagging function, they need to be explicitly explained.
                List<Destructor> destructors = new(Network.DestructorsForFunction(fMsg.Name));
                if (destructors.Count > 0)
                {
                    foreach (Destructor d in destructors)
                    {
                        IMessage lhs = ResolvedNetwork.TermToLooseMessage(d.LeftHandSide);
                        IMessage rhs = ResolvedNetwork.TermToLooseMessage(new(d.RightHandSide));
                        DeconstructionRule dRule = new($"lvs{dId}", lhs, rhs, CellName);
                        dId++;
                        yield return dRule;
                        yield return new LetSetRule(
                            uniqueDesig,
                            Premises,
                            PreviousSockets,
                            NextSockets,
                            IfBranchConditions.Empty,
                            Event.Know(dRule.SourceCellContaining(ResolvedNetwork.TermToMessage(t))));
                    }
                }
                else
                {
                    yield return new LetSetRule(
                            uniqueDesig,
                            Premises,
                            PreviousSockets,
                            NextSockets,
                            IfBranchConditions.Empty,
                            Event.Know(new FunctionMessage(CellName, new() { ResolvedNetwork.TermToMessage(t) })));
                }
            }
            else
            {
                yield return new LetSetRule(
                    uniqueDesig,
                    Premises,
                    PreviousSockets,
                    NextSockets,
                    IfBranchConditions.Empty,
                    Event.Know(new FunctionMessage(CellName, new() { ResolvedNetwork.TermToMessage(t) })));
            }
        }
        else if (iGen is IfTerm it)
        {
            BranchRestrictionSet brSet = BranchRestrictionSet.From(it.Comparison, ResolvedNetwork, Network);
            foreach (IfBranchConditions ifCond in brSet.IfConditions)
            {
                foreach (MutateRule imr in InnerGenerateSetRules(it.TrueTermValue, branchCond.And(ifCond)))
                {
                    yield return imr;
                }
            }
            foreach (IfBranchConditions elseCond in brSet.ElseConditions)
            {
                foreach (MutateRule emr in InnerGenerateSetRules(it.FalseTermValue, branchCond.And(elseCond)))
                {
                    yield return emr;
                }
            }
        }
        else
        {
            string msg = $"No let process to translation exists for {Let.RightHandSide.GetType()}.";
            throw new NotImplementedException(msg);
        }
    }

}

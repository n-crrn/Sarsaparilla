using System;
using System.Collections.Generic;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class InfiniteReadRule : IMutateRule
{

    public InfiniteReadRule(ReadSocket s, string varName, IDictionary<Socket, int> finActionCounts)
        : this(s, varName, new List<string>() { varName }, finActionCounts)
    { }

    public InfiniteReadRule(
        ReadSocket s,
        string varName,
        IReadOnlyList<string> rxPattern,
        IDictionary<Socket, int> finActionCounts)
    {
        Socket = s;
        VariableName = varName;
        ReceivePattern = rxPattern;
        FiniteActionCounts = new Dictionary<Socket, int>(finActionCounts);
    }

    public ReadSocket Socket { get; init; }

    public IReadOnlyList<string> ReceivePattern { get; init; }

    public string VariableName { get; init; }

    public IDictionary<Socket, int> FiniteActionCounts { get; init; }

    public static IEnumerable<IMutateRule> GenerateRulesForReceivePattern(
        ReadSocket rs,
        List<(string, string)> rxPattern,
        IDictionary<Socket, int> finActionCounts)
    {
        List<string> simplifiedRxPattern = new(from rx in rxPattern select rx.Item1);
        foreach (string varName in simplifiedRxPattern)
        {
            yield return new InfiniteReadRule(rs, varName, simplifiedRxPattern, finActionCounts);
        }
    }

    #region IMutateRule implementation.

    public string Label => $"InfRead:{Socket}-{VariableName}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        foreach ((Socket s, int ic) in FiniteActionCounts)
        {
            Snapshot finSS = s.RegisterHistory(factory, ic);
            if (s is ReadSocket)
            {
                Snapshot nextSS = factory.RegisterState(s.WaitingState());
                nextSS.SetModifiedOnceLaterThan(finSS);
            }
        }

        IMessage varMsg;
        if (ReceivePattern.Count == 1)
        {
            varMsg = new VariableMessage(VariableName);
        }
        else
        {
            varMsg = new TupleMessage(from rx in ReceivePattern select new VariableMessage(rx));
        }
        Snapshot ss = factory.RegisterState(Socket.ReadState(varMsg));
        factory.GuardStatements = Conditions?.CreateGuard();
        Rule r = factory.CreateStateConsistentRule(FiniteReadRule.VariableCellAsPremise(VariableName));
        return IfBranchConditions.ApplyReplacements(Conditions, r);
    }

    public int RecommendedDepth => 0;

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Infinite read rule from {Socket} to variable {VariableName}.";

    public override bool Equals(object? obj)
    {
        return obj is InfiniteReadRule r &&
            Socket.Equals(r.Socket) &&
            VariableName == r.VariableName &&
            ReceivePattern.SequenceEqual(r.ReceivePattern) &&
            FiniteActionCounts.ToHashSet().SetEquals(r.FiniteActionCounts) &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}

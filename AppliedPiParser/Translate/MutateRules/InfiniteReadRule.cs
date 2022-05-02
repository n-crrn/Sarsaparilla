using System;
using System.Collections.Generic;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class InfiniteReadRule : IMutateRule
{

    public InfiniteReadRule(ReadSocket s, string varName)
        : this(s, varName, new List<string>() { varName })
    {

    }

    public InfiniteReadRule(ReadSocket s, string varName, IReadOnlyList<string> rxPattern)
    {
        Socket = s;
        VariableName = varName;
        ReceivePattern = rxPattern;
    }

    public ReadSocket Socket { get; init; }

    public IReadOnlyList<string> ReceivePattern { get; init; }

    public string VariableName { get; init; }

    public Rule GenerateRule(RuleFactory factory)
    {
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
        return factory.CreateStateConsistentRule(FiniteReadRule.VariableCellAsPremise(VariableName));
    }

    public static IEnumerable<IMutateRule> GenerateRulesForReceivePattern(ReadSocket rs, List<(string, string)> rxPattern)
    {
        List<string> simplifiedRxPattern = new(from rx in rxPattern select rx.Item1);
        foreach (string varName in simplifiedRxPattern)
        {
            yield return new InfiniteReadRule(rs, varName, simplifiedRxPattern);
        }
    }

    #region Basic object overrides.

    public override string ToString() => $"Infinite read rule from {Socket} to variable {VariableName}.";

    public override bool Equals(object? obj)
    {
        return obj is InfiniteReadRule r && 
            Socket.Equals(r.Socket) && 
            VariableName == r.VariableName && 
            ReceivePattern.SequenceEqual(r.ReceivePattern);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}

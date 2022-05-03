using System.Collections.Generic;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class FiniteReadRule : IMutateRule
{

    public FiniteReadRule(ReadSocket s, int prevReads, string varName)
        : this(s, prevReads, varName, new List<string> { varName })
    { }

    public FiniteReadRule(ReadSocket s, int prevReads, string varName, IReadOnlyList<string> rxPattern)
    {
        Socket = s;
        PreviousReadCount = prevReads;
        VariableName = varName;
        ReceivePattern = rxPattern;
    }

    public ReadSocket Socket { get; init; }

    public int PreviousReadCount { get; init; }

    public IReadOnlyList<string> ReceivePattern { get; init; }

    public string VariableName { get; init; }

    public string Label => $"FinRead:{Socket}-{VariableName}";

    public static Event VariableCellAsPremise(string vName)
    {
        IMessage v = new VariableMessage(vName);
        return Event.Know(new FunctionMessage($"{vName}@cell", new() { v }));
    }

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
        Socket.RegisterReadSequence(factory, PreviousReadCount, Socket.ReadState(varMsg));
        return factory.CreateStateConsistentRule(VariableCellAsPremise(VariableName));
    }

    public static IEnumerable<IMutateRule> GenerateRulesForReceivePattern(ReadSocket readSocket, int prevReads, List<(string, string)> rxPattern)
    {
        List<string> simplifiedRxPattern = new(from rx in rxPattern select rx.Item1);
        foreach (string varName in simplifiedRxPattern)
        {
            yield return new FiniteReadRule(readSocket, prevReads, varName, simplifiedRxPattern);
        }
    }

    #region Basic object overrides.

    public override string ToString() => $"Finite read rule from {Socket} to variable {VariableName}.";

    public override bool Equals(object? obj)
    {
        return obj is FiniteReadRule r &&
            Socket.Equals(r.Socket) &&
            PreviousReadCount == r.PreviousReadCount &&
            ReceivePattern.SequenceEqual(r.ReceivePattern) &&
            VariableName.Equals(r.VariableName);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}

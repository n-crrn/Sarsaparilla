using System.Collections.Generic;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class ReadRule : MutateRule
{

    public ReadRule(ReadSocket s, string varName)
        : this(s, varName, new List<string> { varName })
    { }

    public ReadRule(ReadSocket s, string varName, IReadOnlyList<string> rxPattern)
    {
        Socket = s;
        VariableName = varName;
        ReceivePattern = rxPattern;
        Label = $"FinRead:{Socket}-{VariableName}";
        RecommendedDepth = 1;
    }

    public ReadSocket Socket { get; init; }

    public IReadOnlyList<string> ReceivePattern { get; init; }

    public string VariableName { get; init; }

    #region Static convenience methods.

    public static IEnumerable<MutateRule> GenerateRulesForReceivePattern(
        ReadSocket readSocket,
        List<(string, string)> rxPattern)
    {
        List<string> simplifiedRxPattern = new(from rx in rxPattern select rx.Item1);
        foreach (string varName in simplifiedRxPattern)
        {
            yield return new ReadRule(readSocket, varName, simplifiedRxPattern);
        }
    }

    public static string VariableCellName(string vName) => $"{vName}@cell";

    public static Event VariableCellAsPremise(string vName)
    {
        IMessage v = new VariableMessage(vName);
        return Event.Know(new FunctionMessage(VariableCellName(vName), new() { v }));
    }

    #endregion
    #region IMutateRules implementation.

    public override Rule GenerateRule(RuleFactory factory)
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
        factory.RegisterState(Socket.ReadState(varMsg));
        return GenerateStateConsistentRule(factory, VariableCellAsPremise(VariableName));
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Finite read rule from {Socket} to variable {VariableName}.";

    public override bool Equals(object? obj)
    {
        return obj is ReadRule r &&
            Socket.Equals(r.Socket) &&
            ReceivePattern.SequenceEqual(r.ReceivePattern) &&
            VariableName.Equals(r.VariableName) &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}

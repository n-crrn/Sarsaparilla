using System.Collections.Generic;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class ReadRule : IMutateRule
{

    public ReadRule(ReadSocket s, string varName)
        : this(s, varName, new List<string> { varName })
    { }

    public ReadRule(ReadSocket s, string varName, IReadOnlyList<string> rxPattern)
    {
        Socket = s;
        VariableName = varName;
        ReceivePattern = rxPattern;
    }

    public ReadSocket Socket { get; init; }

    public IReadOnlyList<string> ReceivePattern { get; init; }

    public string VariableName { get; init; }

    #region Static convenience methods.

    public static IEnumerable<IMutateRule> GenerateRulesForReceivePattern(
        ReadSocket readSocket,
        List<(string, string)> rxPattern)
    {
        List<string> simplifiedRxPattern = new(from rx in rxPattern select rx.Item1);
        foreach (string varName in simplifiedRxPattern)
        {
            yield return new ReadRule(readSocket, varName, simplifiedRxPattern);
        }
    }

    public static Event VariableCellAsPremise(string vName)
    {
        IMessage v = new VariableMessage(vName);
        return Event.Know(new FunctionMessage($"{vName}@cell", new() { v }));
    }

    #endregion
    #region IMutateRules implementation.

    public string Label => $"FinRead:{Socket}-{VariableName}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

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
        factory.RegisterState(Socket.ReadState(varMsg));
        factory.GuardStatements = Conditions?.CreateGuard();
        Rule r = factory.CreateStateConsistentRule(VariableCellAsPremise(VariableName));
        return IfBranchConditions.ApplyReplacements(Conditions, r);
    }

    public int RecommendedDepth => 1; // Account for the reset of the read state.

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

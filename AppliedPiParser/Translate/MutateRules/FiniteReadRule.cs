﻿using System.Collections.Generic;
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

    #region Static convenience methods.

    public static IEnumerable<IMutateRule> GenerateRulesForReceivePattern(ReadSocket readSocket, int prevReads, List<(string, string)> rxPattern)
    {
        List<string> simplifiedRxPattern = new(from rx in rxPattern select rx.Item1);
        foreach (string varName in simplifiedRxPattern)
        {
            yield return new FiniteReadRule(readSocket, prevReads, varName, simplifiedRxPattern);
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
        Snapshot prevSS = Socket.RegisterReadSequence(factory, PreviousReadCount, Socket.WaitingState());
        Snapshot latestSS = factory.RegisterState(Socket.ReadState(varMsg));
        latestSS.SetModifiedOnceLaterThan(prevSS);
        factory.GuardStatements = Conditions?.CreateGuard();
        Rule r = factory.CreateStateConsistentRule(VariableCellAsPremise(VariableName));
        return IfBranchConditions.ApplyReplacements(Conditions, r);
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Finite read rule from {Socket} to variable {VariableName}.";

    public override bool Equals(object? obj)
    {
        return obj is FiniteReadRule r &&
            Socket.Equals(r.Socket) &&
            PreviousReadCount == r.PreviousReadCount &&
            ReceivePattern.SequenceEqual(r.ReceivePattern) &&
            VariableName.Equals(r.VariableName) &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}

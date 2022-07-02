using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class InfiniteCrossLink : MutateRule
{

    public InfiniteCrossLink(
        WriteSocket fromSocket, 
        ReadSocket toSocket, 
        PathSurveyor.Marker marker,
        HashSet<Event> premises, 
        IMessage result)
    {
        Debug.Assert(fromSocket.IsInfinite && toSocket.IsInfinite);

        From = fromSocket;
        To = toSocket;
        Marker = marker;
        Premises = premises;
        Result = Event.Know(result);
        
        Label = $"InfXLink:{From}-{To}({Result})";
}

    public WriteSocket From { get; private init; }

    public ReadSocket To { get; private init; }

    public PathSurveyor.Marker Marker { get; private init; }

    public HashSet<Event> Premises { get; private init; }

    public Event Result { get; private init; }

    private static int dId = 0;

    public static IEnumerable<MutateRule> GenerateRulesForReceivePatterns(
        WriteSocket from,
        ReadSocket to,
        PathSurveyor.Marker marker,
        HashSet<Event> premises,
        IMessage sent)
    {
        List<DeconstructionRule> dRules = new();
        foreach (List<(string, string)> pattern in to.ReceivePatterns)
        {
            List<string> simplifiedRxPattern = new(from rx in pattern select rx.Item1);
            foreach (string varName in simplifiedRxPattern)
            {
                IMessage rxMsg;
                if (simplifiedRxPattern.Count == 1)
                {
                    rxMsg = new VariableMessage(varName);
                }
                else
                {
                    rxMsg = new TupleMessage(from rx in simplifiedRxPattern 
                                             select new VariableMessage(rx));
                }
                dRules.Add(new(
                    "icl" + dId,
                    rxMsg,
                    new VariableMessage(varName),
                    ReadRule.VariableCellName(varName)
                ));
                dId++;
            }
        }
        foreach (DeconstructionRule dRule in dRules)
        {
            yield return dRule;
            yield return new InfiniteCrossLink(
                from,
                to,
                marker,
                premises,
                dRule.SourceCellContaining(sent));
        }
    }

    #region IMutateRule implementation.

    public override Rule GenerateRule(RuleFactory factory)
    {
        Snapshot fromWait = factory.RegisterState(From.WaitingState());
        Marker.Register(factory);
        factory.RegisterPremises(fromWait, Premises);
        factory.RegisterState(From.WaitingState());
        factory.RegisterState(To.WaitingState());
        return GenerateStateConsistentRule(factory, Result);
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Infinite cross-link between {From} and {To}.";

    public override bool Equals(object? obj)
    {
        return obj is InfiniteCrossLink icl &&
            From.Equals(icl.From) &&
            To.Equals(icl.To) &&
            Marker.Equals(icl.Marker) &&
            Premises.SetEquals(icl.Premises) &&
            Result.Equals(icl.Result) &&
            Equals(Conditions, icl.Conditions);
    }

    public override int GetHashCode() => From.GetHashCode() + To.GetHashCode();

    #endregion

}

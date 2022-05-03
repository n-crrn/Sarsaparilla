using System.Collections.Generic;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class FiniteWriteRule : IMutateRule
{

    public FiniteWriteRule(WriteSocket s, int priorWrites, HashSet<Event> premises, IMessage value)
    {
        Socket = s;
        PriorWriteCount = priorWrites;
        ValueToWrite = value;
        Premises = premises;
    }

    public WriteSocket Socket { get; init; }

    public int PriorWriteCount { get; init; }

    HashSet<Event> Premises { get; init; }

    public IMessage ValueToWrite { get; init; }

    public Rule GenerateRule(RuleFactory factory)
    {
        Snapshot latest;
        if (PriorWriteCount > 0)
        {
            latest = Socket.RegisterWriteSequence(factory, PriorWriteCount, Socket.WaitingState());
        }
        else
        {
            latest = factory.RegisterState(Socket.InitialState());
        }
        factory.RegisterPremises(latest, Premises);
        latest.TransfersTo = Socket.WriteState(ValueToWrite);
        return factory.CreateStateTransferringRule();
    }

    #region Basic object override.

    public override string ToString() => $"Write to socket rule for {Socket} after {PriorWriteCount} prior writes.";

    public override bool Equals(object? obj)
    {
        return obj is FiniteWriteRule r &&
            Socket.Equals(r.Socket) &&
            PriorWriteCount == r.PriorWriteCount &&
            Premises.SetEquals(r.Premises) &&
            ValueToWrite.Equals(r.ValueToWrite);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}

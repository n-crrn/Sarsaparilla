using System.Collections.Generic;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class InfiniteWriteRule : IMutateRule
{
    public InfiniteWriteRule(WriteSocket s, HashSet<Event> premises, IMessage value)
    {
        Socket = s;
        Premises = new(premises); // Copy, so additional premises are not added.
        ValueToWrite = value;
    }

    public WriteSocket Socket { get; init; }

    public HashSet<Event> Premises { get; init; }

    public IMessage ValueToWrite { get; init; }

    #region IMutateRule implementation.

    public string Label => $"InfWrite:{Socket}-{ValueToWrite}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        Snapshot latest = factory.RegisterState(Socket.AnyState());
        factory.RegisterPremises(latest, Premises);
        latest.TransfersTo = Socket.WriteState(ValueToWrite);
        factory.GuardStatements = Conditions?.CreateGuard();
        return IfBranchConditions.ApplyReplacements(Conditions, factory.CreateStateTransferringRule());
    }

    #endregion
    #region Basic object override.

    public override string ToString() => $"Write to infinite socket rule for {Socket} with {ValueToWrite}.";

    public override bool Equals(object? obj)
    {
        return obj is InfiniteWriteRule r &&
            Socket.Equals(r.Socket) &&
            Premises.SetEquals(r.Premises) &&
            ValueToWrite.Equals(r.ValueToWrite) &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}

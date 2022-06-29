using System.Diagnostics;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class ReadResetRule : IMutateRule
{

    public ReadResetRule(ReadSocket readSocket, PathSurveyor.Marker? marker = null)
    {
        Socket = readSocket;
        Marker = marker;
    }

    public ReadSocket Socket { get; private init; }

    public PathSurveyor.Marker? Marker { get; private init; }

    #region IMutate implementation.

    public string Label => $"ReadReset:{Socket}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        Snapshot priorReads;
        if (Marker != null)
        {
            priorReads = Marker.RegisterAndRetrieve(factory, Socket);
        }
        else
        {
            priorReads = factory.RegisterState(Socket.ReadState(new VariableMessage("@v")));
        }
        priorReads.TransfersTo = Socket.WaitingState();
        return factory.CreateStateTransferringRule();
    }

    public int RecommendedDepth => 0; // The reset is counted as part of the initial read.

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Read reset rule for {Socket}.";

    public override bool Equals(object? obj)
    {
        return obj is ReadResetRule r &&
            Socket.Equals(r.Socket) &&
            Equals(Marker, r.Marker) &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}

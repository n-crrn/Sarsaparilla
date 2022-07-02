using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class ReadResetRule : MutateRule
{

    public ReadResetRule(ReadSocket readSocket, PathSurveyor.Marker? marker = null)
    {
        Socket = readSocket;
        Marker = marker;
        Label = $"ReadReset:{Socket}";
    }

    public ReadSocket Socket { get; private init; }

    public PathSurveyor.Marker? Marker { get; private init; }

    #region IMutate implementation.

    public override Rule GenerateRule(RuleFactory factory)
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
        return GenerateStateTransferringRule(factory);
    }

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

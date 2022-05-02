using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class ShutRule : IMutateRule
{

    public ShutRule(Socket s, int interactionCount)
    {
        Socket = s;
        InteractionCount = interactionCount;
    }

    public Socket Socket { get; init; }

    public int InteractionCount { get; init; }

    public Rule GenerateRule(RuleFactory factory)
    {
        Snapshot prior;
        if (Socket.Direction == SocketDirection.In)
        {
            prior = ((ReadSocket)Socket).RegisterReadSequence(factory, InteractionCount);
        }
        else
        {
            prior = ((WriteSocket)Socket).RegisterWriteSequence(factory, InteractionCount, Socket.WaitingState());
        }
        prior.TransfersTo = Socket.ShutState();
        return factory.CreateStateTransferringRule();
    }

    #region Basic object overrides.

    public override string ToString() => $"Shut rule for {Socket} after {InteractionCount} interactions.";

    public override bool Equals(object? obj) => obj is ShutRule sr && Socket.Equals(sr.Socket) && InteractionCount == sr.InteractionCount;

    public override int GetHashCode() => Socket.GetHashCode() + InteractionCount;

    #endregion

}

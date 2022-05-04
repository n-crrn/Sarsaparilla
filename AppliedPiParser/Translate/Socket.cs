using System.Collections.Generic;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate;

public enum SocketDirection { In, Out }

public abstract class Socket
{

    protected Socket(string name, SocketDirection dir, int branch = Infinite)
    {
        ChannelName = name;
        BranchId = branch;
        Direction = dir;
    }

    public string ChannelName { get; init; }

    public int BranchId { get; init; }

    public const int Infinite = -1;

    public bool IsInfinite => BranchId == Infinite;

    public SocketDirection Direction { get; init; }

    public abstract Snapshot RegisterHistory(RuleFactory factory, int interactions);

    #region Standard socket states.

    public State AnyState() => new(ToString(), new VariableMessage("@Any"));

    public State InitialState() => new(ToString(), new NameMessage("@Initial"));

    public State ReadState(IMessage readValue) => new(ToString(), new FunctionMessage("@Read", new() { readValue }));

    public State WriteState(IMessage writtenValue) => new(ToString(), new FunctionMessage("@Write", new() { writtenValue }));

    public State WaitingState() => new(ToString(), new NameMessage("@Waiting"));

    public State ShutState() => new(ToString(), new NameMessage("@Shut"));

    #endregion
    #region Convenience registration methods.

    protected static void LinkSnapshotList(List<Snapshot> ssList)
    {
        for (int i = 1; i < ssList.Count; i++)
        {
            ssList[i].SetModifiedOnceLaterThan(ssList[i - 1]);
        }
    }

    #endregion
    #region Basic object overrides.

    public override string ToString()
    {
        return ChannelName + "@" + (BranchId == Infinite ? "" : $"{BranchId}@") + Direction;
    }

    public override bool Equals(object? obj)
    {
        return obj is Socket s && ChannelName.Equals(s.ChannelName) && BranchId == s.BranchId && Direction == s.Direction;
    }

    public override int GetHashCode() => ChannelName.GetHashCode() + 7901 * BranchId;

    #endregion

}

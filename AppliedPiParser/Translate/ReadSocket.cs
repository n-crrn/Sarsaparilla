using System.Collections.Generic;
using System.Diagnostics;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate;

public class ReadSocket : Socket
{
    public ReadSocket(string name, int branch = Socket.Infinite) :
        base(name, SocketDirection.In, branch)
    { }

    public Snapshot RegisterReadSequence(RuleFactory factory, int readCount, State? endWith = null)
    {
        Debug.Assert(Direction == SocketDirection.In);
        List<Snapshot> allSS = new() { factory.RegisterState(InitialState()) };
        for (int i = 0; i < readCount; i++)
        {
            factory.RegisterState(WaitingState());
            IMessage readMsg = new VariableMessage($"_v{i}");
            allSS.Add(factory.RegisterState(ReadState(readMsg)));
        }
        if (endWith != null)
        {
            allSS.Add(factory.RegisterState(endWith));
        }
        LinkSnapshotList(allSS);
        return allSS[^1];
    }

    public List<List<(string, string)>> ReceivePatterns { get; init; } = new();

}

﻿using System.Collections.Generic;
using System.Diagnostics;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate;

public class WriteSocket : Socket
{
    public WriteSocket(string name, int branch = Socket.Infinite) :
        base(name, SocketDirection.Out, branch)
    { }

    public override Snapshot RegisterHistory(RuleFactory factory, int interactions)
    {
        if (interactions == 0)
        {
            return RegisterWriteSequence(factory, interactions, null);
        }
        return RegisterWriteSequence(factory, interactions, WaitingState());
    }

    public Snapshot RegisterWriteSequence(RuleFactory factory, int writeCount, State? endWith = null)
    {
        Debug.Assert(Direction == SocketDirection.Out);
        List<Snapshot> allSS = new() { factory.RegisterState(InitialState()), factory.RegisterState(WaitingState()) };
        for (int i = 0; i < writeCount; i++)
        {
            IMessage writeMsg = new VariableMessage($"@v{i}");
            allSS.Add(factory.RegisterState(WriteState(writeMsg)));
            if (i != writeCount - 1)
            {
                allSS.Add(factory.RegisterState(WaitingState()));
            }
        }
        if (endWith != null)
        {
            allSS.Add(factory.RegisterState(endWith));
        }
        LinkSnapshotList(allSS);
        return allSS[^1];
    }
}

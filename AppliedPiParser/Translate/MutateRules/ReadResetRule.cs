using System.Diagnostics;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class ReadResetRule : IMutateRule
{

    public ReadResetRule(ReadSocket readSocket)
    {
        Socket = readSocket;
        Debug.Assert(Socket.IsInfinite);
    }

    public ReadResetRule(ReadSocket readSocket, int readCount)
    {
        Socket = readSocket;
        ReadCount = readCount;
    }

    public ReadSocket Socket { get; init; }

    public int ReadCount { get; init; } = -1;

    #region IMutate implementation.

    public string Label => $"ReadReset:{Socket}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        Snapshot priorReads;
        if (ReadCount >= 0)
        {
            priorReads = Socket.RegisterReadSequence(factory, ReadCount);
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
            ReadCount == r.ReadCount &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode() + ReadCount;

    #endregion

}

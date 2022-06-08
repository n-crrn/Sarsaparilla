﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class WriteRule : IMutateRule
{

    public WriteRule(
        WriteSocket s, 
        IDictionary<Socket, int> finActionCounts, 
        HashSet<Event> premises, 
        IMessage value)
    {
        Socket = s;
        FiniteActionCounts = new Dictionary<Socket, int>(finActionCounts);
        ValueToWrite = value;
        Premises = new(premises); // Copy, so that premises are not added afterwards.
    }

    public WriteSocket Socket { get; init; }

    public IDictionary<Socket, int> FiniteActionCounts { get; init; }

    public HashSet<Event> Premises { get; init; }

    public IMessage ValueToWrite { get; init; }

    #region IMutableRule implementation.

    public string Label
    {
        get
        {
            if (Socket.IsInfinite)
            {
                return $"Write-{ValueToWrite}-{Socket}";
            }
            int priorWriteCount = FiniteActionCounts[Socket]; // Error if not included.
            return $"Write-{ValueToWrite}-{Socket}({priorWriteCount})";
        }
    } 

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        Snapshot? latest = null;
        foreach ((Socket s, int ic) in FiniteActionCounts)
        {
            Snapshot ss = s.RegisterHistory(factory, ic);
            if (s.Equals(Socket))
            {
                latest = ss;
            }
        }
        if (latest == null)
        {
            latest = factory.RegisterState(Socket.WaitingState());
        }
        factory.RegisterPremises(latest, Premises);
        latest.TransfersTo = Socket.WriteState(ValueToWrite);
        factory.GuardStatements = Conditions?.CreateGuard();
        return IfBranchConditions.ApplyReplacements(Conditions, factory.CreateStateTransferringRule());
    }

    public int RecommendedDepth => 2;

    #endregion
    #region Basic object override.

    public override string ToString() => $"Write to socket rule for {Socket} of value {ValueToWrite}.";

    public override bool Equals(object? obj)
    {
        return obj is WriteRule r &&
            Socket.Equals(r.Socket) &&
            FiniteActionCounts.ToHashSet().SetEquals(r.FiniteActionCounts) &&
            Premises.SetEquals(r.Premises) &&
            ValueToWrite.Equals(r.ValueToWrite) &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}
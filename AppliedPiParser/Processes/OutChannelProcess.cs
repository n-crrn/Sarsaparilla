﻿using System;
using System.Collections.Generic;
using System.Linq;
using AppliedPi.Model;

namespace AppliedPi.Processes;

public class OutChannelProcess : IProcess
{
    public OutChannelProcess(string channelName, Term sent, RowColumnPosition? definedAt)
    {
        Channel = channelName;
        SentTerm = sent;
        DefinedAt = definedAt;
    }

    public string Channel { get; init; }

    public Term SentTerm { get; init; }

    #region IProcess implementation.

    public IProcess SubstituteTerms(IReadOnlyDictionary<string, string> subs)
    {
        Term cTerm = new(Channel);        
        return new OutChannelProcess(
            cTerm.ResolveTerm(subs).Name,
            SentTerm.ResolveTerm(subs), DefinedAt);
    }

    public IEnumerable<string> VariablesDefined() => Enumerable.Empty<string>();

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher)
    {
        return Enumerable.Empty<IProcess>();
    }

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        if (!termResolver.Resolve(new(Channel), out TermOriginRecord? tr))
        {
            errorMessage = $"Channel {Channel} not recognised.";
            return false;
        }
        if (!tr!.Type.IsChannel)
        {
            errorMessage = $"Output term {Channel} is not a channel, but used as one.";
            return false;
        }
        if (!termResolver.Resolve(SentTerm, out TermOriginRecord? _))
        {
            errorMessage = $"Sent term {SentTerm} not recognised.";
            return false;
        }
        errorMessage = null;
        return true;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        resolver.ResolveOrThrow(new(Channel));
        resolver.ResolveOrThrow(SentTerm);
        return this;
    }

    public RowColumnPosition? DefinedAt { get; private init; }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is OutChannelProcess ocp && Channel == ocp.Channel && SentTerm == ocp.SentTerm;
    }

    public override int GetHashCode() => Channel.GetHashCode();

    public static bool operator ==(OutChannelProcess p1, OutChannelProcess p2) => Equals(p1, p2);

    public static bool operator !=(OutChannelProcess p1, OutChannelProcess p2) => !Equals(p1, p2);

    public override string ToString() => $"out ({Channel}, {SentTerm})";

    #endregion
}

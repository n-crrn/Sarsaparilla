using System;
using System.Collections.Generic;
using System.Linq;

using AppliedPi.Model;

namespace AppliedPi.Processes;

public class InChannelProcess : IProcess
{
    public InChannelProcess(
        string channelName, 
        List<(string, string)> pattern, 
        RowColumnPosition? definedAt)
    {
        Channel = channelName;
        ReceivePattern = pattern;
        DefinedAt = definedAt;
    }

    public string Channel { get; init; }

    public List<(string, string)> ReceivePattern { get; init; }

    #region IProcess implementation.

    public IProcess SubstituteTerms(IReadOnlyDictionary<string, string> subs)
    {
        Term cTerm = new(Channel);
        List<(string, string)> newPat = 
            new(from rp in ReceivePattern
                select (subs.GetValueOrDefault(rp.Item1, rp.Item1), rp.Item2));
        return new InChannelProcess(cTerm.ResolveTerm(subs).Name, newPat, DefinedAt);
    }

    public IEnumerable<string> VariablesDefined()
    {
        foreach ((string varName, string _) in ReceivePattern)
        {
            yield return varName;
        }
    }

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher)
    {
        return Enumerable.Empty<IProcess>();
    }

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        if (!termResolver.Resolve(new(Channel), out TermOriginRecord? tr))
        {
            errorMessage = $"Input channel {Channel} not recognised.";
            return false;
        }
        if (!tr!.Type.IsChannel)
        {
            errorMessage = $"Attempt to use {Channel} as input channel.";
            return false;
        }
        foreach ((string varName, string piType) in ReceivePattern)
        {
            Term varTerm = new(varName);
            termResolver.Register(varTerm, new(TermSource.Input, new(piType)));
        }
        errorMessage = null;
        return true;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        resolver.ResolveOrThrow(new(Channel));
        foreach ((string varName, string piType) in ReceivePattern)
        {
            resolver.Register(new(varName), new(TermSource.Input, new(piType)));
        }
        return this;
    }

    public RowColumnPosition? DefinedAt { get; }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is InChannelProcess icp && Channel == icp.Channel && ReceivePattern.SequenceEqual(icp.ReceivePattern);
    }

    public override int GetHashCode() => Channel.GetHashCode();

    public static bool operator ==(InChannelProcess p1, InChannelProcess p2) => Equals(p1, p2);

    public static bool operator !=(InChannelProcess p1, InChannelProcess p2) => !Equals(p1, p2);

    public override string ToString()
    {
        if (ReceivePattern.Count == 0)
        {
            return $"in({Channel})";
        }
        else if (ReceivePattern.Count == 1)
        {
            (string inName, string inPiType) = ReceivePattern[0];
            return $"in({Channel}, {inName} : {inPiType})";
        }
        else
        {
            List<string> formattedPattern = new();
            foreach ((string name, string piType) in ReceivePattern)
            {
                formattedPattern.Add($"{name} : {piType}");
            }
            return "in(" + Channel + ", (" + string.Join(", ", formattedPattern) + ")";
        }
    }

    #endregion
}

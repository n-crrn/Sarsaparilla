using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Processes;

public class InChannelProcess : IProcess
{
    public InChannelProcess(string channelName, List<(string, string)> pattern)
    {
        Channel = channelName;
        ReceivePattern = pattern;
    }

    public string Channel { get; init; }

    public List<(string, string)> ReceivePattern { get; init; }

    #region IProcess implementation.

    public IEnumerable<string> Terms() => from rp in ReceivePattern select rp.Item1;

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        List<(string, string)> newPat = new(from rp in ReceivePattern
                                            select (subs.GetValueOrDefault(rp.Item1, rp.Item1), rp.Item2));
        return new InChannelProcess(Channel, newPat);
    }

    public IEnumerable<string> VariablesDefined()
    {
        foreach ((string varName, string _) in ReceivePattern)
        {
            yield return varName;
        }
    }

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

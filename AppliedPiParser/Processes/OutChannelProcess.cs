using AppliedPi.Model;

namespace AppliedPi.Processes;

public class OutChannelProcess : IProcess
{
    public OutChannelProcess(string channelName, Term sent)
    {
        Channel = channelName;
        SentTerm = sent;
    }

    public override bool Equals(object? obj)
    {
        return obj is OutChannelProcess ocp && Channel == ocp.Channel && SentTerm == ocp.SentTerm;
    }

    public override int GetHashCode() => Channel.GetHashCode();

    public static bool operator ==(OutChannelProcess p1, OutChannelProcess p2) => Equals(p1, p2);

    public static bool operator !=(OutChannelProcess p1, OutChannelProcess p2) => !Equals(p1, p2);

    public override string ToString() => $"out ({Channel}, {SentTerm})";

    public string Channel { get; init; }

    public Term SentTerm { get; init; }

    public IProcess? Next { get; set; }
}

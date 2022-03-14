using System.Collections.Generic;
using AppliedPi.Model;

namespace AppliedPi.Processes;

/// <summary>
/// Indicates that an event has been achieved in a process.
/// </summary>
public class EventProcess : IProcess
{
    public EventProcess(Term ev)
    {
        Event = ev;
    }

    public Term Event { get; init; }

    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is EventProcess ep && Event == ep.Event;
    }

    public override int GetHashCode() => Event.GetHashCode();

    public static bool operator ==(EventProcess ep1, EventProcess ep2) => Equals(ep1, ep2);

    public static bool operator !=(EventProcess ep1, EventProcess ep2) => !Equals(ep1, ep2);

    public override string ToString() => $"event {Event}";

    #endregion
    #region IProcess implementation.

    public IProcess? Next { get; set; }

    public IEnumerable<string> Terms() => Event.BasicSubTerms;

    public IProcess ResolveTerms(SortedList<string, string> subs) => new EventProcess(Event.ResolveTerm(subs));

    #endregion
}

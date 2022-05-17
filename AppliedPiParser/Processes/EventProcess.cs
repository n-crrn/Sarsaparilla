using System;
using System.Collections.Generic;
using System.Linq;
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

    #region IProcess implementation.

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs) => new EventProcess(Event.ResolveTerm(subs));

    public IEnumerable<string> VariablesDefined() => Enumerable.Empty<string>();

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher) => Enumerable.Empty<IProcess>();

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        // Check that the event exists.
        if (!nw.Events.TryGetValue(Event.Name, out Event? ev))
        {
            errorMessage = $"Event '{Event.Name}' not declared.";
            return false;
        }

        // Ensure event parameters have been resolved, and that their types match.
        if (Event.Parameters.Count != ev.ParameterTypes.Count)
        {
            errorMessage = $"Event declared with {Event.Parameters.Count} parameters, called with {ev.ParameterTypes.Count}.";
            return false;
        }
        for (int i = 0; i < ev.ParameterTypes.Count; i++)
        {
            Term paraTerm = Event.Parameters[i];
            if (termResolver.Resolve(paraTerm, out TermOriginRecord? tr))
            {
                if (tr!.Type.Name != ev.ParameterTypes[i])
                {
                    errorMessage = $"Parameter {i} was expected to be {ev.ParameterTypes[i]}, found {tr!.Type.Name}.";
                    return false;
                }
            }
            else
            {
                errorMessage = $"Term {paraTerm} could not be resolved.";
                return false;
            }
        }
        errorMessage = null;
        return true;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        foreach (Term p in Event.Parameters)
        {
            resolver.ResolveOrThrow(p);
        }
        return this;
    }

    #endregion
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
}

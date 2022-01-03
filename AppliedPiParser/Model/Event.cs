using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Model;

public class Event
{

    public Event(string n, List<string> pTypes)
    {
        Name = n;
        ParameterTypes = pTypes;
    }

    public string Name { get; init; }

    public List<string> ParameterTypes { get; init; }

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Event ev &&
            Name.Equals(ev.Name) &&
            ParameterTypes.SequenceEqual(ev.ParameterTypes);
    }

    public override int GetHashCode() => Name.GetHashCode();

    public override string ToString() => Name + "(" + string.Join(", ", ParameterTypes) + ")";

}

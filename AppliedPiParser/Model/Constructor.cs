using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Model;

public class Constructor
{
    public Constructor(string nm, List<string> paramTypes, string tpe)
    {
        Name = nm;
        ParameterTypes = paramTypes;
        PiType = tpe;
    }

    public string Name { get; init; }

    public override string ToString() => $"fun {Name}";

    public List<string> ParameterTypes { get; init; }

    public string PiType { get; init; }

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Constructor c &&
            Name.Equals(c.Name) &&
            ParameterTypes.SequenceEqual(c.ParameterTypes) &&
            PiType.Equals(c.PiType);
    }

    public override int GetHashCode() => Name.GetHashCode();
}

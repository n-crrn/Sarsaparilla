using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Model;

public class Constructor
{
    public Constructor(string nm, List<string> paramTypes, string tpe, bool isPrivate)
    {
        Name = nm;
        ParameterTypes = paramTypes;
        PiType = tpe;
        DeclaredPrivate = isPrivate;
    }

    public string Name { get; init; }

    public override string ToString() => $"fun {Name}";

    public List<string> ParameterTypes { get; init; }

    public string PiType { get; init; }

    public bool DeclaredPrivate { get; init; }

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Constructor c &&
            Name.Equals(c.Name) &&
            ParameterTypes.SequenceEqual(c.ParameterTypes) &&
            PiType.Equals(c.PiType) &&
            DeclaredPrivate == c.DeclaredPrivate;
    }

    public override int GetHashCode() => Name.GetHashCode();
}

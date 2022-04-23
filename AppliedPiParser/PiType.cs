using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppliedPi;

public class PiType
{

    public PiType(string basicDesc)
    {
        Name = basicDesc;
        Atoms = new List<PiType>();
    }

    public static PiType Tuple(IEnumerable<PiType> subTypes)
    {
        PiType p = new("");
        ((List<PiType>)p.Atoms).AddRange(subTypes);
        return p;
    }

    public string Name;

    public readonly IReadOnlyList<PiType> Atoms;

    public bool IsComposite => Atoms.Count > 0;

    public bool IsChannel => this == Channel;

    public bool IsBool => this == Bool;

    public bool IsBasicType(string typeName) => !IsComposite && typeName == Name;

    #region Inbuilt types.

    public static readonly PiType Channel = new("channel");

    public static readonly PiType Bool = new("bool");

    public static readonly PiType BitString = new("bitstring");

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is PiType p && Name == p.Name && Atoms.SequenceEqual(p.Atoms);
    }

    public static bool operator==(PiType? p1, PiType? p2) => Equals(p1, p2);

    public static bool operator!=(PiType? p1, PiType? p2) => !Equals(p1, p2);

    public override int GetHashCode()
    {
        int hc = 7901 * 7907 + Name.GetHashCode();
        foreach (PiType p in Atoms)
        {
            hc = hc * 7907 + p.GetHashCode();
        }
        return hc;
    }

    public override string ToString()
    {
        return IsComposite ? Name + "(" + string.Join(", ", Atoms) + ")" : Name;
    }

    #endregion
}

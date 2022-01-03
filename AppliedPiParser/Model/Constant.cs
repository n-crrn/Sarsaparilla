namespace AppliedPi.Model;

public class Constant
{
    public Constant(string n, string t, string tag)
    {
        Name = n;
        PiType = t;
        Tag = tag;
    }

    public string Name { get; init; }

    public string PiType { get; init; }

    public string Tag { get; init; }

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Constant c &&
            Name.Equals(c.Name) &&
            PiType.Equals(c.PiType) &&
            Tag.Equals(c.Tag);
    }

    public override int GetHashCode() => Name.GetHashCode();
}

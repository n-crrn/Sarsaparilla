namespace AppliedPi.Model;

public class StateCell
{
    public StateCell(string name, Term initValue, string? type = null)
    {
        Name = name;
        InitialValue = initValue;
        Type = type;
    }

    public string Name { get; init; }

    public string? Type { get; init; }

    public Term InitialValue { get; init; }

    public override bool Equals(object? obj)
    {
        return obj is StateCell sc &&
            Name.Equals(sc.Name) &&
            string.Equals(Type, sc.Type) &&
            InitialValue.Equals(sc.InitialValue);
    }

    public override int GetHashCode() => Name.GetHashCode();
}

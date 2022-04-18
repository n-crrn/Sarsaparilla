namespace AppliedPi.Model;

public class StateCell
{
    public StateCell(string name, Term initValue, string type)
    {
        Name = name;
        InitialValue = initValue;
        PiType = type;
    }

    public string Name { get; init; }

    public string PiType { get; init; }

    public Term InitialValue { get; init; }

    public override bool Equals(object? obj)
    {
        return obj is StateCell sc &&
            Name.Equals(sc.Name) &&
            string.Equals(PiType, sc.PiType) &&
            InitialValue.Equals(sc.InitialValue);
    }

    public override int GetHashCode() => Name.GetHashCode();
}

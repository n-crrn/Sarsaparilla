namespace AppliedPi.Model;

public class FreeDeclaration
{

    public FreeDeclaration(string n, string type, bool isPriv)
    {
        Name = n;
        PiType = type;
        IsPrivate = isPriv;
    }

    public string Name { get; init; }

    public string PiType { get; init; }

    public bool IsPrivate { get; init; }

    public RowColumnPosition? DefinedAt { get; init; }

    #region Basic object override.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is FreeDeclaration fd &&
            Name.Equals(fd.Name) &&
            PiType.Equals(fd.PiType) &&
            IsPrivate == fd.IsPrivate;
    }

    public override int GetHashCode() => Name.GetHashCode();

    public override string ToString() => $"free {Name}: {PiType}" + (IsPrivate ? "[private]" : "");

    #endregion

}

namespace AppliedPi.Processes;

/// <summary>
/// Creates a nonce value set in a variable.
/// </summary>
public class NewProcess : IProcess
{
    public NewProcess(string varName, string type)
    {
        Variable = varName;
        PiType = type;
    }

    public override bool Equals(object? obj)
    {
        return obj is NewProcess np && Variable == np.Variable && PiType == np.PiType;
    }

    public override int GetHashCode() => Variable.GetHashCode();

    public static bool operator ==(NewProcess proc1, NewProcess proc2) => Equals(proc1, proc2);

    public static bool operator !=(NewProcess proc1, NewProcess proc2) => !Equals(proc1, proc2);

    public override string ToString() => $"new {Variable}: {PiType}";

    public string Variable { get; init; }

    public string PiType { get; init; }

    public IProcess? Next { get; set; }
}

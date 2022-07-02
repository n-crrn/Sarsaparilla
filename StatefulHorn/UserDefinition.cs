namespace StatefulHorn;

/// <summary>
/// A representation of how a user has specified a rule, whether directly or in another modelling
/// language that has been translated to Stateful Horn Clauses.
/// </summary>
public class UserDefinition
{

    public UserDefinition(string src)
    {
        Source = src;
    }

    public UserDefinition(int row, int col, string src)
    {
        Row = row;
        Column = col;
        Source = src;
    }

    public int Row { get; private init; }

    public int Column { get; private init; }

    public string Source { get; private init; }

    public override string ToString() => $"Line {Row}, Col {Column} : {Source}";

}

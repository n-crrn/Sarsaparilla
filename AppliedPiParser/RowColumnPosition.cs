namespace AppliedPi;

/// <summary>
/// Represents a human-readable position within a text file.
/// </summary>
public class RowColumnPosition
{
    public int Row { get; init; }

    public int Column { get; init; }

    public RowColumnPosition(int r, int c)
    {
        Row = r;
        Column = c;
    }

    public override string ToString()
    {
        return $"(Row {Row}, Column {Column})";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Model;

public class Table
{

    public Table(string n, List<string> cols)
    {
        Name = n;
        Columns = cols;
    }

    public string Name { get; init; }

    public List<string> Columns { get; init; }

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Table t &&
            Name.Equals(t.Name) &&
            Columns.SequenceEqual(t.Columns);
    }

    public override int GetHashCode() => Name.GetHashCode();

    public override string ToString() => Name + "(" + string.Join(", ", Columns) + ")";

}

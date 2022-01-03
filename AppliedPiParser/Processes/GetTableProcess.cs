using System.Collections.Generic;

namespace AppliedPi.Processes;

public class GetTableProcess : IProcess
{
    public GetTableProcess(string table, List<(bool, string)> maList)
    {
        TableName = table;
        MatchAssignList = maList;
    }

    public override string ToString()
    {
        List<string> formattedMatches = new();
        foreach ((bool isMatch, string name) in MatchAssignList)
        {
            formattedMatches.Add(isMatch ? "=" + name : name);
        }
        return $"get {TableName}(" + string.Join(", ", formattedMatches) + ")";
    }

    public string TableName { get; init; }

    public List<(bool, string)> MatchAssignList;

    public IProcess? Next { get; set; }
}

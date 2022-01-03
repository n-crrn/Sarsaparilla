using System;
using System.Collections.Generic;
using System.Linq;

namespace AppliedPi;

public class UserDefinedProcess
{
    public UserDefinedProcess(string name, List<(string Name, string PiType)> paras, ProcessGroup procs)
    {
        Name = name;
        Parameters = paras;
        Processes = procs;
    }

    public string Name { get; init; }

    public List<(string Name, string PiType)> Parameters { get; init; }

    public ProcessGroup Processes { get; init; }

    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is UserDefinedProcess udp &&
            Name.Equals(udp.Name) &&
            Parameters.SequenceEqual(udp.Parameters) &&
            Processes.Equals(udp.Processes);
    }

    public override int GetHashCode() => Name.GetHashCode();

    public string Title
    {
        get
        {
            String allParams = string.Join(", ", from p in Parameters select $"{p.Name}: {p.PiType}");
            return $"{Name}({allParams})";
        }
    }

    public override string ToString() => Title;

    #endregion
}

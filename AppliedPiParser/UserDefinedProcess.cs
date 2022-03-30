using System;
using System.Collections.Generic;
using System.Linq;

using AppliedPi.Model;

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

    /// <summary>
    /// User friendly description of the process, including its name and list of parameters.
    /// </summary>
    public string Title
    {
        get
        {
            String allParams = string.Join(", ", from p in Parameters select $"{p.Name}: {p.PiType}");
            return $"{Name}({allParams})";
        }
    }

    public ProcessGroup ResolveForCall(int callIndex, List<string> parameterValues)
    {
        if (parameterValues.Count != Parameters.Count)
        {
            throw new ArgumentException("Number of parameter values must match number of parameters");
        }

        SortedList<string, string> subs = new();

        // Add in the paramters.
        for (int i = 0; i < parameterValues.Count; i++)
        {
            subs[Parameters[i].Name] = parameterValues[i];
        }

        // Update the variables defined during the run.
        string prefix = callIndex == 0 ? $"{Name}@" : $"{Name}@{callIndex}@";
        foreach (string varName in Processes.VariablesDefined())
        {
            subs[varName] = prefix + varName;
        }

        return (ProcessGroup)Processes.ResolveTerms(subs);
    }

    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is UserDefinedProcess udp &&
            Name.Equals(udp.Name) &&
            Parameters.SequenceEqual(udp.Parameters) &&
            Processes.Equals(udp.Processes);
    }

    public override int GetHashCode() => Name.GetHashCode();

    public override string ToString() => Title;

    #endregion
}

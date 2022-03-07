using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using AppliedPi.Model;
using AppliedPi.Processes;

namespace AppliedPi;

/// <summary>
/// A Network is a fully detailed, complete system of communicating processes. This is the 
/// model object for further analysis of a system.
/// </summary>
public class Network
{

    public Network()
    {
        _PiTypes = new() { "bitstring" };
        _FreeDeclarations = new();
        _Constants = new();
        _Tables = new();
        _Events = new();
        _Queries = new();
        _Constructors = new();
        _Destructors = new();
        _LetDefinitions = new();
    }

    public static Network CreateFromCode(string appliedPiCode)
    {
        Network nw = new();
        Parser p = new(appliedPiCode);
        ParseResult result = p.ReadNextStatement();
        while (result.Successful)
        {
            result.Statement?.ApplyTo(nw);
            result = p.ReadNextStatement();
        }

        // Check that we haven't had a failure.
        if (!result.AtEnd)
        {
            throw new NetworkCreationException(result.ErrorMessage, result.ErrorPosition!);
        }

        return nw;
    }

    #region PiType declarations.

    internal List<string> _PiTypes;

    public IReadOnlyList<string> PiTypes { get => _PiTypes.ToImmutableList(); }

    #endregion
    #region Free name declarations.

    internal List<FreeDeclaration> _FreeDeclarations;

    public IReadOnlyList<FreeDeclaration> FreeDeclarations { get => _FreeDeclarations; }

    #endregion
    #region Constant declarations.

    internal List<Constant> _Constants;

    public IReadOnlyList<Constant> Constants { get => _Constants; }

    #endregion
    #region Table declarations.

    internal List<Table> _Tables;

    public IReadOnlyList<Table> Tables { get => _Tables; }

    #endregion
    #region Event declarations.

    internal List<Event> _Events;

    public IReadOnlyList<Event> Events { get => _Events; }

    #endregion
    #region Query declarations.

    internal List<Query> _Queries;

    public IReadOnlyList<Query> Queries { get => _Queries; }

    #endregion
    #region Constructor and destructor declarations.

    internal List<Constructor> _Constructors;
    internal List<Destructor> _Destructors;

    public IReadOnlyList<Constructor> Constructors => _Constructors;

    public IReadOnlyList<Destructor> Destructors => _Destructors;

    #endregion
    #region Processes (including let statements).

    private readonly SortedList<string, UserDefinedProcess> _LetDefinitions;
    public ProcessGroup? MainProcess;

    // FIXME: Include a method to return a "compiled" process, that includes all the let declarations
    // to have the appropriate substitutions done and replaced into the MainProcess.

    internal void AddUserDefinedProcess(UserDefinedProcess proc)
    {
        _LetDefinitions[proc.Name] = proc;
    }

    public bool HasDefinitionFor(string name) => _LetDefinitions.ContainsKey(name);

    public IReadOnlyDictionary<string, UserDefinedProcess> LetDefinitions => _LetDefinitions;

    #endregion
    #region Model coherence checking.

    public (bool, string? errMsg) Lint()
    {
        // Is a main process defined?
        if (MainProcess == null)
        {
            return (false, "Main process not defined");
        }

        // Go through every process (including the main one) and ensure that every CallProcess is
        // calling an existing process with the correct number of arguments.
        (bool subProcExist, string? subProcErr) = AllSubProcessesExist(MainProcess);
        if (!subProcExist)
        {
            return (false, $"Error validating main process: {subProcErr}");
        }
        foreach ((string name, UserDefinedProcess udp) in _LetDefinitions)
        {
            (subProcExist, subProcErr) = AllSubProcessesExist(udp.Processes);
            if (!subProcExist)
            {
                return (false, $"Error validating {name} process: {subProcErr}");
            }
        }

        (bool recursionFound, string? recurseDesc) = CheckForRecursion();
        if (recursionFound)
        {
            return (false, $"Recursion detected: {recurseDesc}.");
        }

        // FIXME: Add type-checking.

        return (true, null);
    }

    private List<CallProcess> FindAllSubProcessCalls(ProcessGroup pg)
    {
        List<CallProcess> foundCP = new();
        foreach ((IProcess p, bool _) in pg.Processes)
        {
            if (p is CallProcess cp)
            {
                foundCP.Add(cp);
            }
            else if (p is IfProcess ip)
            {
                AddIfProcessCalls(ip, foundCP);
            }
            else if (p is ParallelCompositionProcess pcp)
            {
                AddParallelProcessCalls(pcp, foundCP);
            }
        }
        return foundCP;
    }

    private void AddIfProcessCalls(IfProcess ip, List<CallProcess> foundCP)
    {
        if (ip.GuardedProcess is CallProcess icp)
        {
            foundCP.Add(icp);
        }
        else if (ip.GuardedProcess is ProcessGroup ipg)
        {
            foundCP.AddRange(FindAllSubProcessCalls(ipg));
        }

        IProcess? elseIP = ip.ElseProcess;
        if (elseIP != null)
        {
            if (elseIP is CallProcess eicp)
            {
                foundCP.Add(eicp);
            }
            else if (elseIP is ProcessGroup eipg)
            {
                foundCP.AddRange(FindAllSubProcessCalls(eipg));
            }
        }
    }

    private void AddParallelProcessCalls(ParallelCompositionProcess pcp, List<CallProcess> foundCP)
    {
        foreach ((IProcess innerP, bool _) in pcp.Processes)
        {
            if (innerP is CallProcess pcp_cp)
            {
                foundCP.Add(pcp_cp);
            }
            else if (innerP is IfProcess pcp_ip)
            {
                AddIfProcessCalls(pcp_ip, foundCP);
            }
            else if (innerP is ParallelCompositionProcess pcp_pcp)
            {
                AddParallelProcessCalls(pcp_pcp, foundCP);
            }
        }
    }

    private (bool, string?) AllSubProcessesExist(ProcessGroup proc)
    {
        List<CallProcess> allCP = FindAllSubProcessCalls(proc);

        foreach (CallProcess cp in allCP)
        {
            string procName = cp.CallSpecification.Name;
            if (!_LetDefinitions.TryGetValue(cp.CallSpecification.Name, out UserDefinedProcess? up))
            {
                return (false, $"No process defined with name {procName}.");
            }
            int actualParaCount = cp.CallSpecification.Parameters.Count;
            int expectedParaCount = up.Parameters.Count;
            if (actualParaCount != expectedParaCount)
            {
                return (false, $"Expected {expectedParaCount} parameters, found {actualParaCount} parameters in call to {procName}.");
            }
            // FIXME: In future check that the paramter types match.
        }

        return (true, null);
    }

    private (bool, string?) CheckForRecursion()
    {
        Dictionary<string, HashSet<string>> calledNames = new();

        // Build a dictionary of sets of each let statements names.
        foreach ((string name, UserDefinedProcess udp) in _LetDefinitions)
        {
            List<CallProcess> allCalls = FindAllSubProcessCalls(udp.Processes);
            HashSet<string> calledProcesses = new(from ac in allCalls select ac.CallSpecification.Name);
            if (calledProcesses.Contains(name))
            {
                return (true, name);
            }
            calledNames[name] = calledProcesses;
        }

        Stack<string> callStack = new();
        foreach (string name in calledNames.Keys)
        {
            string? find = FoundRecursion(callStack, name, calledNames);
            if (find != null)
            {
                return (true, $"{find} within callstack for {name}");
            }
        }
        return (false, null);
    }

    private string? FoundRecursion(Stack<string> callStack, string nextCall, Dictionary<string, HashSet<string>> calledNames)
    {
        if (callStack.Contains(nextCall))
        {
            return nextCall;
        }
        HashSet<string> nextCalledItems = calledNames[nextCall];
        if (nextCalledItems.Count == 0)
        {
            return null;
        }
        callStack.Push(nextCall);
        foreach (string item in nextCalledItems)
        {
            string? find = FoundRecursion(callStack, item, calledNames);
            if (find != null)
            {
                return find; // Don't bother with resetting stack, not important.
            }
        }
        callStack.Pop(); // Get nextCall off the stack.
        return null;
    }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Network nw &&
            _PiTypes.SequenceEqual(nw._PiTypes) &&
            _FreeDeclarations.SequenceEqual(nw._FreeDeclarations) &&
            _Constants.SequenceEqual(nw._Constants) &&
            _Tables.SequenceEqual(nw._Tables) &&
            _Events.SequenceEqual(nw._Events) &&
            _Queries.SequenceEqual(nw._Queries) &&
            _Constructors.SequenceEqual(nw._Constructors) &&
            _Destructors.SequenceEqual(nw._Destructors);
    }

    public override int GetHashCode() => (Constructors, Destructors).GetHashCode();

    #endregion
}

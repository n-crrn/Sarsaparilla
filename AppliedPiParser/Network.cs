﻿using System;
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

    public Network() { }

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

    public static readonly string ChannelType = "channel";
    public static readonly string BitstringType = "bitstring";

    internal HashSet<string> _PiTypes = new() { ChannelType, BitstringType };

    public IReadOnlySet<string> PiTypes { get => _PiTypes.ToImmutableHashSet(); }

    #endregion
    #region Free name declarations.

    internal Dictionary<string, FreeDeclaration> _FreeDeclarations = new();

    public IReadOnlyDictionary<string, FreeDeclaration> FreeDeclarations { get => _FreeDeclarations; }

    #endregion
    #region Constant declarations.

    internal HashSet<Constant> _Constants = new();

    public IReadOnlySet<Constant> Constants { get => _Constants; }

    #endregion
    #region Table declarations.

    internal Dictionary<string, Table> _Tables = new();

    public IReadOnlyDictionary<string, Table> Tables { get => _Tables; }

    #endregion
    #region Event declarations.

    internal Dictionary<string, Event> _Events = new();

    public IReadOnlyDictionary<string, Event> Events { get => _Events; }

    #endregion
    #region Query declarations.

    internal HashSet<Query> _Queries = new();

    public IReadOnlySet<Query> Queries { get => _Queries; }

    #endregion
    #region Constructor and destructor declarations.

    internal Dictionary<string, Constructor> _Constructors = new();
    internal HashSet<Destructor> _Destructors = new();

    public IReadOnlyDictionary<string, Constructor> Constructors => _Constructors;

    public IReadOnlySet<Destructor> Destructors => _Destructors;

    #endregion
    #region Statement initialisation declarations.

    // FIXME: Consideration to be given to making this a dictionary.

    internal HashSet<Term> _InitialStates = new();

    public IReadOnlySet<Term> InitialStates => _InitialStates;

    public Term? GetStateCell(string cellName)
    {
        foreach (Term t in _InitialStates)
        {
            if (t.Name == cellName)
            {
                return t;
            }
        }
        return null;
    }

    #endregion
    #region Processes (including let statements).

    private readonly SortedList<string, UserDefinedProcess> _LetDefinitions = new();
    public ProcessGroup? MainProcess;

    // FIXME: Include a method to return a "compiled" process, that includes all the let declarations
    // to have the appropriate substitutions done and replaced into the MainProcess.

    internal void AddUserDefinedProcess(UserDefinedProcess proc)
    {
        _LetDefinitions[proc.Name] = proc;
    }

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
        // Note that there are no fancy equality tests such as bisimilarity tests.
        // This is a straight forward case of "are the processes defined exactly the same way?"
        return obj != null &&
            obj is Network nw &&
            _PiTypes.SetEquals(nw._PiTypes) &&
            DictionariesMatch(_FreeDeclarations, nw._FreeDeclarations) &&
            _Constants.SetEquals(nw._Constants) &&
            DictionariesMatch(_Tables, nw._Tables) &&
            DictionariesMatch(_Events, nw._Events) &&
            _Queries.SetEquals(nw._Queries) &&
            DictionariesMatch(_Constructors, nw._Constructors) &&
            _Destructors.SetEquals(nw._Destructors) &&
            _InitialStates.SetEquals(nw._InitialStates) &&
            DictionariesMatch(_LetDefinitions, nw._LetDefinitions) &&
            ProcessGroup.Equals(MainProcess, nw.MainProcess);
    }

    private static bool DictionariesMatch<T1,T2>(IReadOnlyDictionary<T1,T2> d1, IReadOnlyDictionary<T1,T2> d2)
    {
        return d1.Count == d2.Count && !(d1.Except(d2).Any());
    }

    public override int GetHashCode() => (Constructors, Destructors).GetHashCode();

    #endregion
}

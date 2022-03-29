using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using AppliedPi.Model;
using AppliedPi.Processes;

namespace AppliedPi;

public enum TermSource
{
    Free,
    Constant,
    Nonce,
    Input,
    Let,
    Table,
    Constructor
}

/// <summary>
/// Describes a complete process. That is, a single (composite) process that completely describes
/// a network. All terms have an associated type, and all variables have something (name, nonce or
/// read input value) assigned to them.
/// </summary>
public class ResolvedNetwork
{

    public Dictionary<Term, (TermSource Source, string PiType)> TermDetails { get; } = new();

    private readonly List<(IProcess Process, bool Replicated)> _ProcessSequence = new();

    public IReadOnlyList<(IProcess Process, bool Replicated)> ProcessSequence => _ProcessSequence;

    public ProcessGroup AsGroup()
    {
        if (_ProcessSequence.Count == 0)
        {
            throw new MemberAccessException("Cannot retrieve process sequence as group if there are no processes");
        }
        return new(_ProcessSequence);
    }

    #region Creating from a network.

    public static ResolvedNetwork From(Network nw)
    {
        ProcessGroup? main = nw.MainProcess;
        if (main == null)
        {
            throw new ArgumentException("Network does not have a main process.");
        }

        ResolvedNetwork rp = new();

        // Add constants.
        foreach (Constant c in nw.Constants)
        {
            Term ct = new(c.Name);
            rp.TermDetails[ct] = (TermSource.Constant, c.PiType);
        }

        foreach ((IProcess process, bool repl) in main.Processes)
        {
            IProcess updatedProcess = rp.ResolveProcess(process, nw);
            rp._ProcessSequence.Add((updatedProcess, repl));
        }
        return rp;
    }

    private IProcess ResolveProcess(IProcess p, Network nw)
    {
        IProcess toInclude = p;
        switch (p)
        {
            case CallProcess cp:
                toInclude = ResolveCallProcess(cp, nw);
                break;
            case EventProcess evp:
                ResolveEventProcess(evp, nw);
                break;
            case IfProcess ip:
                toInclude = ResolveIfProcess(ip, nw);
                break;
            case InChannelProcess icp:
                ResolveInChannelProcess(icp, nw);
                break;
            case OutChannelProcess ocp:
                ResolveOutChannelProcess(ocp, nw);
                break;
            case LetProcess lp:
                ResolveLetProcess(lp, nw);
                break;
            case NewProcess np:
                ResolveNewProcess(np);
                break;
            case ParallelCompositionProcess pcp:
                toInclude = ResolveParallelCompositionProcess(pcp, nw);
                break;
            case GetTableProcess gtp:
                ResolveGetTableProcess(gtp, nw);
                break;
            case InsertTableProcess itp:
                ResolveInsertTableProcess(itp, nw);
                break;
            case ProcessGroup pg:
                toInclude = ResolveProcessGroup(pg, nw);
                break;
        }
        return toInclude;
    }

    // Used to ensure that the terms within a call process are renamed correctly.
    private readonly Dictionary<string, int> CallProcessCounter = new();

    private IProcess ResolveCallProcess(CallProcess cp, Network nw)
    {
        if (!nw.LetDefinitions.TryGetValue(cp.Name, out UserDefinedProcess? udp))
        {
            throw new ArgumentException($"No user defined process with name {cp.Name}.");
        }
        // Check that parameters line up.
        if (udp.Parameters.Count != cp.CallSpecification.Parameters.Count)
        {
            throw new ArgumentException($"Call to {cp.Name} does not have the correct number of parameters.");
        }
        for (int i = 0; i < cp.CallSpecification.Parameters.Count; i++)
        {
            Term paramSpec = cp.CallSpecification.Parameters[i];
            // Check that the call specification term is defined.
            if (!TermDetails.TryGetValue(paramSpec, out (TermSource Source, string PiType) termValue))
            {
                throw new ArgumentException($"Parameter {paramSpec} does not exist.");
            }

            // Check that it is the correct type.
            (string paramName, string piType) = udp.Parameters[i];
            if (piType != termValue.PiType)
            {
                throw new ArgumentException($"Parameter {paramSpec} does not have the correct type in call to {udp.Name}.");
            }
        }

        // Rename variables and terms to fit with rest of process.
        CallProcessCounter.TryGetValue(udp.Name, out int cpc);
        CallProcessCounter[udp.Name] = cpc + 1;
        List<string> parameters = new(from c in cp.CallSpecification.Parameters select c.ToString());
        ProcessGroup varRenamedGroup = udp.ResolveForCall(cpc, parameters);

        // Fully resolve the sub-process.
        List<(IProcess Process, bool Replicated)> updatedGroupDetails = new();
        foreach ((IProcess p, bool r) in varRenamedGroup.Processes)
        {
            updatedGroupDetails.Add((ResolveProcess(p, nw), r));
        }
        return new ProcessGroup(updatedGroupDetails);
    }

    private void ResolveEventProcess(EventProcess evp, Network nw)
    {
        // All subTerms should be defined before the event is called.
        if (!nw.Events.TryGetValue(evp.Event.Name, out Event? ev))
        {
            throw new ArgumentException($"Attempted to reference non-existant event '{evp.Event.Name}'.");
        }
        for (int i = 0; i < evp.Event.Parameters.Count; i++)
        {
            Term subTerm = evp.Event.Parameters[i];
            if (TermDetails.TryGetValue(subTerm, out (TermSource Source, string PiType) details))
            {
                if (details.PiType != ev.ParameterTypes[i])
                {
                    throw new ArgumentException($"Types do not match for parameter {i + 1} of event {evp.Event.Name}.");
                }
            }
            else
            {
                throw new ArgumentException($"Term {subTerm} not defined.");
            }
        }
    }

    private IfProcess ResolveIfProcess(IfProcess ip, Network nw)
    {
        foreach (string vars in ip.Comparison.Variables)
        {
            Term varTerm = new(vars);
            // All terms must exist to be valid.
            if (!TermDetails.ContainsKey(varTerm))
            {
                throw new ArgumentException($"Term {varTerm} does not exist.");
            }
        }
        IProcess newGuarded = ResolveProcess(ip.GuardedProcess, nw);
        IProcess? newElse = null;
        if (ip.ElseProcess != null)
        {
            newElse = ResolveProcess(ip.ElseProcess, nw);
        }
        return new(ip.Comparison, newGuarded, newElse);
    }

    private void ResolveInChannelProcess(InChannelProcess icp, Network nw)
    {
        if (!CheckChannel(nw, icp.Channel))
        {
            throw new ArgumentException($"Channel {icp.Channel} not properly defined in call to 'in'.");
        }
        foreach ((string varName, string piType) in icp.ReceivePattern)
        {
            Term varTerm = new(varName);
            if (TermDetails.ContainsKey(varTerm))
            {
                throw new ArgumentException($"Term {varName} redefined in call to 'in'.");
            }
            TermDetails[varTerm] = (TermSource.Input, piType);
        }
    }

    private void ResolveOutChannelProcess(OutChannelProcess ocp, Network nw)
    {
        if (!CheckChannel(nw, ocp.Channel))
        {
            throw new ArgumentException($"Channel {ocp.Channel} not properly defined in call to 'out'.");
        }
        if (!CheckTermDetailsValid(ocp.SentTerm, nw, this))
        {
            throw new ArgumentException($"Invalid term {ocp.SentTerm}.");
        }
    }

    private bool CheckChannel(Network nw, string channelName)
    {
        if (nw.FreeDeclarations.TryGetValue(channelName, out FreeDeclaration? fd))
        {
            Debug.Assert(fd != null);
            if (fd.PiType == Network.ChannelType)
            {
                TermDetails[new Term(channelName)] = (TermSource.Free, Network.ChannelType);
                return true;
            }
        }
        return false;
    }

    private void ResolveLetProcess(LetProcess lp, Network nw)
    {
        // Unfortunately, we cannot exhaustively check the types in these assignments. The
        // authoritative implementation (ProVerif) allows some absurd happenings within its
        // let processes.
        foreach (Term matchTerm in lp.LeftHandSide.MatchTerms)
        {
            if (!TermDetails.ContainsKey(matchTerm))
            {
                throw new ArgumentException($"Required term {matchTerm} from let statement matching element not defined.");
            }
        }
        // According to the ProVerif manual, the type can be omitted if it can be inferred.
        // Yeah ... that won't be tolerated here.
        foreach ((Term assignedTerm, string? piType) in lp.LeftHandSide.AssignedTerms)
        {
            if (piType == null)
            {
                throw new ArgumentException($"Type for assigned term {assignedTerm} not specified.");
            }
            TermDetails[assignedTerm] = (TermSource.Let, piType);
        }
        foreach (string rhsSubTermStr in lp.RightHandSide.BasicSubTerms)
        {
            Term rhsSubTerm = new(rhsSubTermStr);
            ResolveTerm(rhsSubTerm, nw);
        }
    }

    private void ResolveNewProcess(NewProcess np)
    {
        Term newTerm = new(np.Variable);
        if (TermDetails.ContainsKey(newTerm))
        {
            throw new ArgumentException($"Term {newTerm} has already been defined.");
        }
        TermDetails[newTerm] = (TermSource.Nonce, np.PiType);
    }

    private ParallelCompositionProcess ResolveParallelCompositionProcess(ParallelCompositionProcess pcp, Network nw)
    {
        return new(from pr in pcp.Processes select ResolveParallelSubProcess(pr.Process, pr.Replicated, nw));
    }

    private (IProcess, bool) ResolveParallelSubProcess(IProcess p, bool repl, Network nw)
    {
        IProcess rp = ResolveProcess(p, nw);
        if (rp is ProcessGroup pg && pg.Processes.Count == 1)
        {
            return (pg.Processes[0].Process, repl || pg.Processes[0].Replicated);
        }
        return (rp, repl);
    }

    private void ResolveGetTableProcess(GetTableProcess gtp, Network nw)
    {
        if (!nw.Tables.TryGetValue(gtp.TableName, out Table? table))
        {
            throw new ArgumentException($"Attempted to reference table '{gtp.TableName}'.");
        }
        Debug.Assert(table != null);
        if (gtp.MatchAssignList.Count != table.Columns.Count)
        {
            throw new ArgumentException($"Mismatch in count of columns for table {gtp.TableName} and match assign terms.");
        }
        for (int i = 0; i < gtp.MatchAssignList.Count; i++)
        {
            (bool match, string assignTermDesc) = gtp.MatchAssignList[i];
            Term term = new(assignTermDesc);
            if (!ResolveTerm(term, nw))
            {
                if (match)
                {
                    // This is an issue - for this to be a matcher, it should have been defined.
                    throw new ArgumentException($"Table matching term {term} has not been defined.");
                }
                TermDetails[term] = (TermSource.Table, table.Columns[i]);
            }
        }
    }

    private void ResolveInsertTableProcess(InsertTableProcess itp, Network nw)
    {
        if (!nw.Tables.TryGetValue(itp.TableName, out Table? table))
        {
            throw new ArgumentException($"Attempted to reference table '{itp.TableName}'");
        }
        Debug.Assert(table != null);
        if (itp.WriteTerms.Count != table.Columns.Count)
        {
            throw new ArgumentException($"Mismatch in count of columns for table {itp.TableName} and write terms.");
        }
        for (int i = 0; i < itp.WriteTerms.Count; i++)
        {
            Term writeTerm = itp.WriteTerms[i];
            if (!CheckTermDetailsValid(writeTerm, nw, this))
            {
                throw new ArgumentException($"Term {writeTerm} is not valid.");
            }
            string piType = writeTerm.IsConstructed ? nw.Constructors[writeTerm.Name].PiType : TermDetails[writeTerm].PiType;
            if (piType != table.Columns[i])
            {
                throw new ArgumentException($"Write term does not have correct type (expected {table.Columns[i]}, got {piType}).");
            }
        }
    }

    private IProcess ResolveProcessGroup(ProcessGroup pg, Network nw)
    {
        List<(IProcess Process, bool Replicated)> resolved = new(from pr in pg.Processes
                                                                 select (ResolveProcess(pr.Process, nw), pr.Replicated));
        if (resolved.Count == 1 && !(resolved[0].Replicated))
        {
            // If there is only one sub-process without replication, we can promote it.
            return resolved[0].Process;
        }
        return new ProcessGroup(resolved);
    }

    private bool ResolveTerm(Term t, Network nw)
    {
        // Check the existing details, which will include nonces and variables added by other
        if (TermDetails.ContainsKey(t))
        {
            return true;
        }

        if (t.Parameters.Count == 0)
        {
            if (nw.FreeDeclarations.TryGetValue(t.Name, out FreeDeclaration? freeDecl))
            {
                TermDetails[t] = (TermSource.Free, freeDecl.PiType);
                return true;
            }

            // Search for term in the constants declarations.
            foreach (Constant con in nw.Constants)
            {
                if (con.Name == t.Name)
                {
                    TermDetails[t] = (TermSource.Constant, con.PiType);
                }
            }

            // Does not exist.
            return false;
        }
        else
        {
            foreach (Term para in t.Parameters)
            {
                if (!ResolveTerm(para, nw))
                {
                    // Parameter term does not exist.
                    return false;
                }
            }
            if (t.IsTuple)
            {
                // Don't worry about recording the whole tuple term details.
                return true;
            }

            // Term is some sort of function call/constructor - check call details.
            if (nw.Constructors.TryGetValue(t.Name, out Constructor? ctr))
            {
                TermDetails[t] = (TermSource.Constructor, ctr.PiType);
                return true;
            }
            return false;
        }
    }

    private static bool CheckTermDetailsValid(Term t, Network nw, ResolvedNetwork rp)
    {
        if (t.IsConstructed)
        {
            if (nw.Constructors.TryGetValue(t.Name, out Constructor? ctor))
            {
                if (ctor.ParameterTypes.Count != t.Parameters.Count)
                {
                    return false;
                }
                for (int i = 0; i < t.Parameters.Count; i++)
                {
                    Term innerTerm = t.Parameters[i];
                    if (innerTerm.IsConstructed)
                    {
                        if (!nw.Constructors.TryGetValue(innerTerm.Name, out Constructor? innerCtr))
                        {
                            // Non-existance inner term.
                            return false;
                        }
                        if (innerCtr.PiType != ctor.ParameterTypes[i] || !CheckTermDetailsValid(innerTerm, nw, rp))
                        {
                            // Either the constructor is not the correct type or is internally inconsistent.
                            return false;
                        }
                    }
                    else if (innerTerm.IsTuple)
                    {
                        // We don't do tuples within constructors.
                        return false;
                    }
                    else
                    {
                        if (!rp.TermDetails.TryGetValue(innerTerm, out (TermSource Source, string PiType) innerTermDetails))
                        {
                            // Inner term not previously pulled in by a process - let's see if it
                            // is a valid item and pull it in if required.
                            (bool found, string? piType) = TryResolveBasicTerm(innerTerm, nw, rp);
                            if (!found || !string.Equals(piType, ctor.ParameterTypes[i]))
                            {
                                return false;
                            }
                        }
                        else if (innerTermDetails.PiType != ctor.ParameterTypes[i])
                        {
                            // The inner variable type does not match the parameter type.
                            return false;
                        }
                    }
                }
            }
            else
            {
                return false;
            }
        }
        else if (t.IsTuple)
        {
            foreach (Term tuplePara in t.Parameters)
            {
                if (!CheckTermDetailsValid(tuplePara, nw, rp))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static (bool, string?) TryResolveBasicTerm(Term t, Network nw, ResolvedNetwork rp)
    {
        Debug.Assert(!t.IsConstructed);
        // Check constants.
        foreach (Constant c in nw.Constants)
        {
            if (c.Name == t.Name)
            {
                rp.TermDetails[t] = (TermSource.Constant, c.PiType);
                return (true, c.PiType);
            }
        }
        // Check free-names.
        // FIXME: Does the "private" data need to be noted here?
        foreach ((string _, FreeDeclaration fd) in nw.FreeDeclarations)
        {
            if (fd.Name == t.Name)
            {
                rp.TermDetails[t] = (TermSource.Free, fd.PiType);
                return (true, fd.PiType);
            }
        }
        return (false, null);
    }

    #endregion
    #region Debugging.

    /// <summary>
    /// A method to allow the direct setting of term details and sequence. This method is intended
    /// to be used by unit tests to ensure that other ResolvedProcess methods are working
    /// correctly.
    /// </summary>
    /// <param name="termDetails">
    /// A dictionary relating terms with whether they are names, nonces or inputs, and what 
    /// Applied Pi type they are.
    /// </param>
    /// <param name="sequence">
    /// The resolved sequence of processes. Note that that the "Next" 
    /// </param>
    public void DirectSet(
        Dictionary<Term, (TermSource, string)> termDetails,
        List<(IProcess Process, bool Replicated)> sequence)
    {
        Debug.Assert(TermDetails.Count == 0 && _ProcessSequence.Count == 0);
        foreach ((Term t, (TermSource, string) details) in termDetails)
        {
            TermDetails[t] = details;
        }
        _ProcessSequence.AddRange(sequence);
    }

    public void Describe(TextWriter writer)
    {
        writer.WriteLine("--- Resolved terms are  ---");
        foreach ((Term t, (TermSource src, string piType)) in TermDetails)
        {
            writer.WriteLine($"\t{t}\t{src}\t{piType}");
        }
        writer.WriteLine("--- Process sequence is ---");
        foreach ((IProcess p, bool repl) in _ProcessSequence)
        {
            string replPrefix = repl ? "!" : "";
            writer.WriteLine($"{replPrefix}\t{p}");
        }
        writer.WriteLine("---------------------------");
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        if (obj is not ResolvedNetwork rp || 
            TermDetails.Count != rp.TermDetails.Count ||
            _ProcessSequence.Count != rp._ProcessSequence.Count)
        {
            return false;
        }
        if (!_ProcessSequence.SequenceEqual(rp._ProcessSequence))
        {
            return false;
        }
        foreach ((Term t, (TermSource, string) details) in TermDetails)
        {
            if (!rp.TermDetails.TryGetValue(t, out (TermSource, string) otherDetails) ||
                details != otherDetails)
            {
                return false;
            }
        }
        return true;
    }

    public override int GetHashCode() => _ProcessSequence.Count == 0 ? 0 : _ProcessSequence[0].GetHashCode();

    #endregion

}

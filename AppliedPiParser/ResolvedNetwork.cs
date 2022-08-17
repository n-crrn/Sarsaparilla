using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;
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
    Constructor,
    Tuple,
    MacroParameter
}

public record TermOriginRecord(TermSource Source, PiType Type);

/// <summary>
/// Describes a complete process. That is, a single (composite) process that completely describes
/// a network. All terms have an associated type, and all variables have something (name, nonce or
/// read input value) assigned to them.
/// </summary>
public class ResolvedNetwork
{
    public ResolvedNetwork()
    {
        TermDetails = new();
    }

    public Dictionary<Term, TermOriginRecord> TermDetails { get; }

    private readonly List<IProcess> _ProcessSequence = new();

    public IReadOnlyList<IProcess> ProcessSequence => _ProcessSequence;

    private readonly HashSet<IMessage> _Queries = new();

    public IReadOnlySet<IMessage> Queries => _Queries;

    public ProcessGroup AsGroup()
    {
        if (_ProcessSequence.Count == 0)
        {
            throw new MemberAccessException("Cannot retrieve process sequence as group if there are no processes");
        }
        return new(_ProcessSequence, _ProcessSequence[0].DefinedAt);
    }

    public IMessage TermToMessage(Term t)
    {
        if (t.Parameters.Count > 0)
        {
            List<IMessage> msgParams = new(from p in t.Parameters select TermToMessage(p));
            return t.IsTuple ? new TupleMessage(msgParams) : new FunctionMessage(t.Name, msgParams);
        }

        if (!TermDetails.TryGetValue(t, out TermOriginRecord? details))
        {
            throw new UnrecognisedTermException(t);
        }
        (TermSource source, PiType _) = details;
        if (source == TermSource.Input || source == TermSource.Let)
        {
            return new VariableMessage(t.Name);
        }
        return new NameMessage(t.Name);
    }

    /// <summary>
    /// Translates the given term much like TermToMessage(Term). However, if a term is not
    /// recognised as either a free declaration or a constant, that term is assumed to be a
    /// variable rather than an error. This method is used when translating destructors, 
    /// which may reuse variable names.
    /// </summary>
    /// <param name="t">Applied Pi term to translate.</param>
    /// <returns>A Stateful Horn message version of the term.</returns>
    public IMessage TermToLooseMessage(Term t)
    {
        if (t.Parameters.Count > 0)
        {
            List<IMessage> msgParams = new(from p in t.Parameters select TermToLooseMessage(p));
            return t.IsTuple ? new TupleMessage(msgParams) : new FunctionMessage(t.Name, msgParams);
        }

        if (TermDetails.TryGetValue(t, out TermOriginRecord? details) &&
            (details.Source == TermSource.Constant ||
             details.Source == TermSource.Free))
        {
            return new NameMessage(t.Name);
        }
        return new VariableMessage(t.Name);
    }

    public Event TermToKnow(Term t) => Event.Know(TermToMessage(t));

    public bool CheckTermType(Term t, PiType pt)
    {
        if (TermDetails.TryGetValue(t, out TermOriginRecord? details))
        {
            return details.Type.Equals(pt);
        }
        throw new UnrecognisedTermException(t);
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

        // Check phase - ensure everything *should* fit together, starting with the macros.
        foreach (UserDefinedProcess macro in nw.LetDefinitions.Values)
        {
            string? foundErr = CheckMacro(nw, macro);
            if (foundErr != null)
            {
                throw new ArgumentException(foundErr);
            }
        }
        TermResolver tr = new(nw);
        if (!main.Check(nw, tr, out string? errMsg))
        {
            throw new ArgumentException(errMsg);
        }

        // If we are here, the input model looks good. Let's fit it together.
        IProcess updated = main.Resolve(nw, tr);
        if (updated is ProcessGroup updatedPg)
        {
            rp._ProcessSequence.AddRange(updatedPg.Processes);
        }
        else
        {
            rp._ProcessSequence.Add(updated);
        }
        
        foreach ((Term t, TermOriginRecord r) in tr.Registered)
        {
            rp.TermDetails[t] = new(r.Source, r.Type);
        }
        rp.SetQueries(nw.Queries);
        return rp;
    }

    private static string? CheckMacro(Network nw, UserDefinedProcess udp)
    {
        TermResolver tr = new(nw);
        foreach ((string n, string piType) in udp.Parameters)
        {
            tr.Register(new(n), new(TermSource.MacroParameter, new(piType)));
        }
        udp.Processes.Check(nw, tr, out string? errMsg);
        return errMsg;
    }

    private void SetQueries(IReadOnlySet<AttackerQuery> queries)
    {
        foreach (AttackerQuery aq in queries)
        {
            Dictionary<string, List<string>> replacements = new();

            SortedSet<string> basics = aq.LeakQuery.BasicSubTerms;
            foreach (string b in basics)
            {
                Term t = new(b);
                if (TermDetails.TryGetValue(t, out TermOriginRecord? rec))
                {
                    replacements[b] = new List<string>() { b };
                } 
                else 
                {
                    List<string> matches = GetQueryMatchingVariables(b);
                    if (matches.Count == 0)
                    {
                        throw new ArgumentException($"Cannot execute query as term {b} is not modelled in the system.");
                    }
                    replacements[b] = matches;
                }
            }

            // Valid result?
            if (replacements.Count == 1 && replacements.Keys.First() == replacements.Values.First().First())
            {
                _Queries.Add(TermToMessage(aq.LeakQuery));
            }
            else
            {
                // Are they all of the same length?
                List<int> lengthCounts = new();
                foreach ((string _, List<string> r) in replacements)
                {
                    lengthCounts.Add(r.Count);
                }
                int maxValue = lengthCounts.Max();
                bool onesFound = false;
                foreach (string match in replacements.Keys)
                {
                    int thisLength = replacements[match].Count;
                    if (thisLength != 1 && thisLength != maxValue)
                    {
                        throw new ArgumentException($"Cannot execute query as query terms are from multiple macros.");
                    }
                    onesFound |= thisLength == 1;
                }
                
                foreach (string match in replacements.Keys)
                {
                    List<string> replList = replacements[match];
                    while (replList.Count < maxValue)
                    {
                        replList.Add(replList[0]);
                    }
                }
                for (int i = 0; i < maxValue; i++)
                {
                    _Queries.Add(TermToMessage(aq.LeakQuery.ResolveTerm(GetSubstitution(replacements, i))));
                }
            }
        }
    }

    private List<string> GetQueryMatchingVariables(string localName)
    {
        List<string> matches = new();
        foreach ((Term t, TermOriginRecord _) in TermDetails)
        {
            if (!t.IsConstructed && t.Name.EndsWith(localName))
            {
                matches.Add(t.Name);
            }
        }
        matches.Sort();
        return matches;
    }

    private static Dictionary<string, string> GetSubstitution(Dictionary<string, List<string>> matches, int row)
    {
        Dictionary<string, string> subs = new();
        foreach ((string k, List<string> v) in matches)
        {
            subs[k] = v[row];
        }
        return subs;
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
        Dictionary<Term, TermOriginRecord> termDetails,
        List<IProcess> sequence)
    {
        Debug.Assert(TermDetails.Count == 0 && _ProcessSequence.Count == 0);
        foreach ((Term t, TermOriginRecord details) in termDetails)
        {
            TermDetails[t] = details;
        }
        _ProcessSequence.AddRange(sequence);
    }

    public void Describe(TextWriter writer)
    {
        writer.WriteLine("--- Resolved terms are  ---");
        foreach ((Term t, (TermSource src, PiType piType)) in TermDetails)
        {
            writer.WriteLine($"\t{t}\t{src}\t{piType}");
        }
        writer.WriteLine("--- Process sequence is ---");
        writer.WriteLine(string.Join('\n', _ProcessSequence));
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
        foreach ((Term t, TermOriginRecord details) in TermDetails)
        {
            if (!rp.TermDetails.TryGetValue(t, out TermOriginRecord? otherDetails) ||
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

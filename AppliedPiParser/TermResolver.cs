using System;
using System.Collections.Generic;

using AppliedPi.Model;

namespace AppliedPi;

/// <summary>
/// Instances of this class are used as an aid to determine the types of names in a Pi-Calculus
/// network. It uses top-level data from the Network along with names encountered while parsing
/// processes.
/// </summary>
public class TermResolver
{

    /// <summary>
    /// Names and their types that are available to processes in all networks.
    /// </summary>
    public static readonly Dictionary<Term, TermOriginRecord> BuiltInValues = new()
    {
        { new("true"), new(TermSource.Constant, PiType.Bool) },
        { new("false"), new(TermSource.Constant, PiType.Bool) }
    };

    public TermResolver(Network nw)
    {
        Network = nw;
        Registered = new Dictionary<Term, TermOriginRecord>(BuiltInValues);

        foreach ((string _, FreeDeclaration fd) in nw.FreeDeclarations)
        {
            Register(new(fd.Name), new(TermSource.Free, new(fd.PiType)));
        }
        foreach (Constant c in nw.Constants)
        {
            Register(new(c.Name), new(TermSource.Constant, new(c.PiType)));
        }
    }

    private readonly Network Network;

    internal readonly Dictionary<Term, TermOriginRecord> Registered;

    public bool Register(Term t, TermOriginRecord rec) => Registered.TryAdd(t, rec);

    public bool Resolve(Term t, out TermOriginRecord? rec)
    {
        if (Registered.TryGetValue(t, out rec))
        {
            return true;
        }
        rec = null;

        // See if we can resolve from the Network.
        // Note that parameter-less items are added in the constructor.
        if (t.Parameters.Count > 0) 
        {
            // Sub terms need to be resolved before we resolve this one.
            List<PiType> subTypes = new();
            foreach (Term para in t.Parameters)
            {
                if (!Resolve(para, out TermOriginRecord? subRec))
                {
                    return false;
                }
                else
                {
                    subTypes.Add(subRec!.Type);
                }
            }
            if (t.IsTuple)
            {
                rec = new(TermSource.Tuple, PiType.Tuple(subTypes));
            }
            else
            {
                // Check that the appropriate constructor exists. This includes checking that the
                // argument types match.
                if (Network.Constructors.TryGetValue(t.Name, out Constructor? c))
                {
                    if (c.ParameterTypes.Count != subTypes.Count)
                    {
                        return false;
                    }
                    for (int i = 0; i < subTypes.Count; i++)
                    {
                        if (!subTypes[i].ToString().Equals(c.ParameterTypes[i]) &&
                            !(subTypes[i].IsTuple && c.ParameterTypes[i].Equals(PiType.BitString.ToString())))
                        {
                            return false;
                        }
                    }
                    rec = new(TermSource.Constructor, new(c.PiType));
                }
                else
                {
                    return false;
                }
            }
            Register(t, rec);
            return true;
        }
        return false;
    }

    public void ResolveOrThrow(Term t)
    {
        if (!Resolve(t, out TermOriginRecord? _))
        {
            throw new ResolverException(t);
        }
    }

    public bool TryGetRecord(Term t, out TermOriginRecord? record) => Registered.TryGetValue(t, out record);

    private readonly Dictionary<string, int> MacroCallCounter = new();

    public IProcess ResolveMacroCall(string macroName, List<string> parameters)
    {
        if (!Network.LetDefinitions.TryGetValue(macroName, out UserDefinedProcess? udp))
        {
            throw new ArgumentException($"{macroName} is not the name of a valid let-defined process.");
        }

        // Replace the variable names with parameterised names.
        MacroCallCounter.TryGetValue(macroName, out int mcc);
        ProcessGroup pg = udp.ResolveForCall(mcc, parameters);
        MacroCallCounter[macroName] = mcc + 1;
        return pg.Resolve(Network, this);
    }

}

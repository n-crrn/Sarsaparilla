using System;
using System.Collections.Generic;

using AppliedPi.Model;

namespace AppliedPi;

public record TermRecord(TermSource Source, PiType Type);

public class TermResolver
{

    public TermResolver(Network nw)
    {
        Network = nw;
        Registered = new Dictionary<Term, TermRecord>();
    }

    private readonly Network Network;

    internal readonly Dictionary<Term, TermRecord> Registered;

    public bool Register(Term t, TermRecord rec) => Registered.TryAdd(t, rec);

    public bool Resolve(Term t, out TermRecord? rec)
    {
        if (Registered.TryGetValue(t, out rec))
        {
            return true;
        }
        rec = null;

        // See if we can resolve from the Network.
        if (t.Parameters.Count == 0)
        {
            string termName = t.Name;
            // Check the free declarations ...
            if (Network.FreeDeclarations.TryGetValue(termName, out FreeDeclaration? fd))
            {
                rec = new(TermSource.Free, new(fd.PiType));
                Register(t, rec);
                return true;
            }
            // ... then the constant declarations ...
            Constant? possConst = Network.GetConstant(termName);
            if (possConst != null)
            {
                rec = new(TermSource.Constant, new(possConst.PiType) );
                Register(t, rec);
                return true;
            }
            // ... finally the state cells.
            StateCell? cell = Network.GetStateCell(termName);
            if (cell != null)
            {
                rec = new(TermSource.StateCell, new(cell.PiType) );
            }
        }
        else
        {
            // Sub terms need to be resolved before we resolve this one.
            List<PiType> subTypes = new();
            foreach (Term para in t.Parameters)
            {
                if (!Resolve(para, out TermRecord? subRec))
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
                // Check that the appropriate constructor exists.
                if (Network.Constructors.TryGetValue(t.Name, out Constructor? c))
                {
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
        if (!Resolve(t, out TermRecord? _))
        {
            throw new ResolverException(t);
        }
    }

    public bool TryGetRecord(Term t, out TermRecord? record) => Registered.TryGetValue(t, out record);

    private readonly Dictionary<string, int> MacroCallCounter = new();

    public IProcess ResolveMacroCall(string macroName, List<string> parameters)
    {
        if (!Network.LetDefinitions.TryGetValue(macroName, out UserDefinedProcess? udp))
        {
            throw new ArgumentException($"{macroName} is not the name of a valid let-defined process.");
        }

        // Replace the variable names with parameterised names.
        MacroCallCounter.TryGetValue(macroName, out int mcc);
        MacroCallCounter[macroName] = mcc;
        ProcessGroup pg = udp.ResolveForCall(mcc, parameters);
        return pg.Resolve(Network, this);
    }

}

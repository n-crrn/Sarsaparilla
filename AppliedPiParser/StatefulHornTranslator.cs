using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn;
using StatefulHorn.Messages;
using AppliedPi.Model;

namespace AppliedPi;

public static class StatefulHornTranslator
{

    public static StatefulHornTranslation Translate(Network nw)
    {
        // The function "K" is used throughout to succinctly generate StatefulHorn.Event.Know events.
        StatefulHornTranslation sht = new();
        RuleFactory factory = new();

        // Pi Types are not translated.

        // Translate free names and constants.
        IEnumerable<NameMessage> basicNames = from fd in nw.FreeDeclarations 
                                              where !fd.Value.IsPrivate 
                                              select new NameMessage(fd.Key);
        IEnumerable<NameMessage> constNames = from cd in nw.Constants
                                              select new NameMessage(cd.Name);
        Dictionary<string, NameMessage> knownConstants = new();
        foreach (NameMessage nm in basicNames.Concat(constNames))
        {
            sht.Rules.Add(factory.CreateStateConsistentRule(K(nm)));
            knownConstants[nm.Name] = nm;
        }

        // Translate constructors.
        foreach ((string _, Constructor c) in nw.Constructors)
        {
            List<string> paramNames = VariableNamesForTypesList(c.ParameterTypes);
            List<IMessage> paramVarMsgs = new(from p in paramNames select VarOrConstMessage(p, knownConstants));
            factory.RegisterPremises((from pm in paramVarMsgs select K(pm)).ToArray());
            sht.Rules.Add(factory.CreateStateConsistentRule(K(new FunctionMessage(c.Name, paramVarMsgs))));
        }

        // Translate destructors.
        foreach (Destructor d in nw.Destructors)
        {
            IMessage lhsMsg = TermToMessage(d.LeftHandSide, knownConstants);
            IMessage rhsMsg = VarOrConstMessage(d.RightHandSide, knownConstants);
            factory.RegisterPremise(K(lhsMsg));
            sht.Rules.Add(factory.CreateStateConsistentRule(K(rhsMsg)));
        }

        // Need to get a compiled process at this point...
        // FIXME: Write me.

        return sht;
    }

    // Convenience method to save typing.
    private static StatefulHorn.Event K(IMessage msg) => StatefulHorn.Event.Know(msg);

    private static List<string> VariableNamesForTypesList(List<string> types)
    {
        List<string> names = new(types.Count);
        Dictionary<string, int> typeCounter = new();
        foreach (string t in types)
        {
            typeCounter.TryGetValue(t, out int tCount);
            if (tCount == 0)
            {
                names.Add(t);
                tCount = 1;
            }
            else
            {
                names.Add($"{t}-{tCount}");
            }
            typeCounter[t] = tCount + 1;
            
        }
        return names;
    }

    private static IMessage VarOrConstMessage(string term, Dictionary<string, NameMessage> knownConsts)
    {
        if (knownConsts.TryGetValue(term, out NameMessage? foundMsg))
        {
            return foundMsg!;
        }
        return new VariableMessage(term);
    }

    private static IMessage TermToMessage(Term t, Dictionary<string, NameMessage> knownConsts)
    {
        if (t.Parameters.Count == 0)
        {
            return VarOrConstMessage(t.Name, knownConsts);
        }

        List<IMessage> subMsgs = new(from st in t.Parameters select TermToMessage(st, knownConsts));
        return t.IsTuple ? new TupleMessage(subMsgs) : new FunctionMessage(t.Name, subMsgs);
    }

}

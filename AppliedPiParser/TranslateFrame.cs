using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn;

namespace AppliedPi;

public class TranslateFrame
{
    private static readonly RuleFactory Factory = new();

    public TranslateFrame(StateFrame initFrame, HashSet<string> names)
    {
        StateFrames.Add(initFrame);
        Substitutions = new();
        Names = names;
    }

    public TranslateFrame(
        IEnumerable<Event> premises,
        IDictionary<IMessage, IMessage> subs,
        IEnumerable<StateFrame> states,
        HashSet<string> names)
    {
        Premises.UnionWith(premises);
        Substitutions = new(subs);
        StateFrames.UnionWith(states);
        Names = names;
    }

    public HashSet<Event> Premises { get; } = new();

    public Dictionary<IMessage, IMessage> Substitutions { get; }

    public HashSet<StateFrame> StateFrames { get; private set; } = new();

    public HashSet<string> Names { get; init; }

    public TranslateFrame Clone() => new(Premises, Substitutions, StateFrames, Names);

    public void MutateState(State mutated)
    {
        HashSet<StateFrame> updated = new(from sf in StateFrames select sf.Clone().Update(mutated));
        StateFrames = updated;
    }

    public List<StateTransferringRule> CreateTransferRules(State newState)
    {
        List<StateTransferringRule> rules = new();
        foreach (StateFrame sf in StateFrames)
        {
            foreach (State s in sf.Cells)
            {
                Snapshot ss = Factory.RegisterState(s);
                if (s.Name == newState.Name)
                {
                    ss.TransfersTo = newState;
                }
                Factory.RegisterPremises(ss, Premises);
            }
            rules.Add(Factory.CreateStateTransferringRule());
        }
        return rules;
    }

    public List<StateConsistentRule> CreateConsistentRules(Event ev)
    {
        List<StateConsistentRule> rules = new();
        foreach (StateFrame sf in StateFrames)
        {
            List<Snapshot> allSS = new(from s in sf.Cells select Factory.RegisterState(s));
            foreach (Snapshot ss in allSS)
            {
                Factory.RegisterPremises(ss, Premises);
            }
            rules.Add(Factory.CreateStateConsistentRule(ev));
        }
        return rules;
    }

}

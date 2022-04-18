using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn;
using StatefulHorn.Messages;
using AppliedPi.Model;
using AppliedPi.Processes;

namespace AppliedPi;

/// <summary>
/// Represents a completed translation from an Applied Pi source to a series of Stateful Horn
/// clauses. Not every Applied Pi source will result in a valid set of clauses. Therefore,
/// this class allows for holding a partial translation in a way that, for instance, 
/// StatefulHorn.QueryEngine does not.
/// </summary>
public class StatefulHornTranslation
{

    public HashSet<State> InitialState { get; } = new();

    public IMessage? QueryMessage { get; internal set; }

    public State? QueryWhen { get; internal set; }

    public HashSet<Rule> Rules { get; } = new();

    #region Overall translation process.

    public static (StatefulHornTranslation?, string?) Translate(Network nw)
    {
        StatefulHornTranslation trans = new();
        RuleFactory factory = new();
        HashSet<string> names = new();
        HashSet<string> stateCellNames = new();

        // Translate free names and constants to rules without premises.
        IEnumerable<string> rawNames = (from fd in nw.FreeDeclarations
                                        where !fd.Value.IsPrivate
                                        select fd.Key).Concat(from cd in nw.Constants
                                                              select cd.Name);
        names.UnionWith(rawNames);
        foreach (string name in names)
        {
            trans.Rules.Add(factory.CreateStateConsistentRule(K(new NameMessage(name))));
        }

        // Translate constructors.
        foreach ((string _, Constructor c) in nw.Constructors)
        {
            List<IMessage> premises = new(from p in VariableNamesForTypesList(c.ParameterTypes)
                                          select new VariableMessage(p));
            factory.RegisterPremises((from pMsg in premises select K(pMsg)).ToArray());
            trans.Rules.Add(factory.CreateStateConsistentRule(K(new FunctionMessage(c.Name, premises))));
        }

        // Translate destructors.
        foreach (Destructor d in nw.Destructors)
        {
            IMessage lhsMsg = TermToMessage(d.LeftHandSide, names);
            IMessage rhsMsg = new VariableMessage(d.RightHandSide);
            factory.RegisterPremise(K(lhsMsg));
            trans.Rules.Add(factory.CreateStateConsistentRule(K(rhsMsg)));
        }

        if (nw.MainProcess != null)
        {
            // The network needs to be resolved so that it's visible where state operations are needed.
            ResolvedNetwork resNw;
            try
            {
                resNw = ResolvedNetwork.From(nw);
            }
            catch (Exception ex)
            {
                return (null, $"Unable to resolve network: {ex}");
            }

            // FIXME: Complete translation here.
        }

        return (trans, null);
    }

    private static StatefulHorn.Event K(IMessage msg) => StatefulHorn.Event.Know(msg);

    /// <summary>
    /// In typed Applied Pi Calculus, the arguments for a constructor are provided as a list of
    /// types. Given that it is possible for a constructor to have multiple inputs requiring 
    /// the same 
    /// </summary>
    /// <param name="types"></param>
    /// <returns></returns>
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

    private static IMessage TermToMessage(Term t, HashSet<string> knownNames)
    {
        if (t.Parameters.Count == 0)
        {
            return VarOrNameMessage(t.Name, knownNames);
        }
        IEnumerable<IMessage> subMsgs = from st in t.Parameters select TermToMessage(st, knownNames);
        return t.IsTuple ? new TupleMessage(subMsgs) : new FunctionMessage(t.Name, new(subMsgs));
    }

    private static IMessage VarOrNameMessage(string term, HashSet<string> knownNames)
    {
        return knownNames.Contains(term) ? new NameMessage(term) : new VariableMessage(term);
    }

    #endregion
    #region Individual process translations.

    private void Translate(IProcess p, TranslateFrame frame)
    {
        // FIXME: Add InsertTableProcess and GetTableProcess constructs. According to the
        // ProVerif manual, they can be implemented using private channels.
        switch (p)
        {
            case NewProcess np:
                TranslateNew(np, frame);
                break;
            case InChannelProcess icp:
                TranslateInChannel(icp, frame);
                break;
            case OutChannelProcess ocp:
                TranslateOutChannel(ocp, frame);
                break;
            case MutateProcess mp:
                TranslateMutate(mp, frame);
                break;
            case ReplicateProcess rp:
                TranslateReplicate(rp, frame);
                break;
            case ParallelCompositionProcess pcp:
                TranslateParallel(pcp, frame);
                break;
            case ProcessGroup pg:
                foreach (IProcess subP in pg.Processes)
                {
                    Translate(subP, frame);
                }
                break;
            // FIXME: Include LetProcess & IfProcess.
            default:
                throw new NotImplementedException($"Process type {p.GetType()} cannot be translated.");
        };
    }

    private static void TranslateNew(NewProcess np, TranslateFrame frame)
    {
        frame.Substitutions[new VariableMessage(np.Variable)] = NameMessage.Any;
    }

    private void TranslateInChannel(InChannelProcess icp, TranslateFrame frame)
    {
        //frame.Premises.Add(K(icp.))
    }

    private void TranslateOutChannel(OutChannelProcess ocp, TranslateFrame frame)
    {
        // FIXME: Write me.
    }

    private void TranslateMutate(MutateProcess mp, TranslateFrame frame)
    {
        State newState = new(mp.StateCellName, TermToMessage(mp.NewValue, frame.Names));
        Rules.UnionWith(frame.CreateTransferRules(newState));
        frame.MutateState(newState);
    }

    private void TranslateReplicate(ReplicateProcess rp, TranslateFrame frame)
    {
        // Straight pass through...
        Translate(rp.Process, frame);
    }

    private void TranslateParallel(ParallelCompositionProcess pcp, TranslateFrame frame)
    {
        foreach (IProcess p in pcp.Processes)
        {
            Translate(p, frame.Clone());
        }

        /*HashSet<StatefulHorn.Event> resultPremises = new();
        Dictionary<IMessage, IMessage> resultSubstitutions = new();
        HashSet<StateFrame> resultStateFrames = new();

        foreach (IProcess subProcess in pcp.Processes)
        {
            TranslateFrame updateableFrame = frame.Clone();
            Translate(subProcess, updateableFrame);
            resultPremises.UnionWith(updateableFrame.Premises);
            // FIXME: Reconsider whether the following logic is required.
            MergeDictionaries(updateableFrame.Substitutions, resultSubstitutions);
            resultStateFrames.UnionWith(updateableFrame.StateFrames);
        }

        frame.Premises.UnionWith(resultPremises);
        MergeDictionaries(frame.Substitutions, resultSubstitutions);
        frame.StateFrames.UnionWith(resultStateFrames);*/
    }

    private static void MergeDictionaries(
        IDictionary<IMessage, IMessage> mainDict,
        IDictionary<IMessage, IMessage> subsDict)
    {
        foreach ((IMessage varName, IMessage varValue) in subsDict)
        {
            if (!mainDict.ContainsKey(varName))
            {
                mainDict[varName] = varValue;
            }
        }
    }

    #endregion
    #region Basic object overrides.

    private static bool IMessageEquals(IMessage? msg1, IMessage? msg2) => msg1 == null ? msg2 == null : msg1.Equals(msg2);

    public override bool Equals(object? other)
    {
        return other is StatefulHornTranslation sht &&
            InitialState.SetEquals(sht.InitialState) &&
            IMessageEquals(QueryMessage, sht.QueryMessage) &&
            State.Equals(QueryWhen, sht.QueryWhen) &&
            Rules.SetEquals(sht.Rules);
    }

    public static bool operator ==(StatefulHornTranslation sht1, StatefulHornTranslation sht2) => Equals(sht1, sht2);

    public static bool operator !=(StatefulHornTranslation sht1, StatefulHornTranslation sht2) => !Equals(sht1, sht2);

    public override int GetHashCode()
    {
        int hc = 31;
        foreach (State s in InitialState)
        {
            hc = hc * 41 + s.GetHashCode();
        }
        foreach (Rule r in Rules)
        {
            hc = hc * 41 + r.GetHashCode();
        }
        return hc;
    }

    #endregion
    #region Describe translation.

    public void Describe(TextWriter writer)
    {
        writer.WriteLine("Initial state: " + string.Join(", ", InitialState));
        writer.WriteLine($"Query: {QueryMessage}");
        if (QueryWhen != null)
        {
            writer.WriteLine($"When: {QueryWhen}");
        }
        writer.WriteLine("Rules:");
        foreach (Rule r in Rules)
        {
            writer.WriteLine("  " + r.ToString());
        }
    }

    #endregion

}

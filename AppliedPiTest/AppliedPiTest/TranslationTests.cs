using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using AppliedPi;
using AppliedPi.Translate;
using AppliedPi.Translate.MutateRules;
using StatefulHorn;
using StatefulHorn.Messages;

namespace SarsaparillaTests.AppliedPiTest;

[TestClass]
public class TranslationTests
{

    [TestMethod]
    public void TwoBranchTest()
    {
        string testSource =
@"free c: channel.
free x: bitstring [private].
process out(c, x) | in(c, y: bitstring).";

        WriteSocket c2Out = new("c", 2);
        ReadSocket c3In = new("c", 3);
        HashSet<Socket> expectedSockets = new() { c2Out, c3In };
        Dictionary<Socket, int> finiteWriteInteractions = new() { { c2Out, 0} };
        HashSet<IMutateRule> expectedMutations = new()
        {
            new KnowChannelContentRule(c2Out),
            new OpenSocketsRule(c3In),
            new FiniteWriteRule(c2Out, finiteWriteInteractions, new(), new NameMessage("x")),
            new FiniteCrossLinkRule(c2Out, c3In),
            new FiniteReadRule(c3In, 0, "y"),
            new AttackChannelRule("y"),
            new ShutSocketsRule(new Dictionary<Socket, int>() { { c2Out, 1 } }),
            new ShutSocketsRule(new Dictionary<Socket, int>() { { c3In, 1 } })
        };
        DoMutateTest(testSource, expectedSockets, expectedMutations);
    }
    
    /// <summary>
    /// A test to ensure that the lifecycle rules for channels are handled correctly. The test
    /// model used in this test is not meant to be sensible.
    /// </summary>
    [TestMethod]
    public void OpeningShuttingTest()
    {
        string testSource =
@"free c: channel.
free d: bitstring [private].
free e: bitstring [private].

process
    out(c, d);
    out(c, e);
    ( in(c, v: bitstring) |
      (new f: channel;
       out(f, d);
       ! ( in(c, x: bitstring); out(c, f) ) ) ).
";

        WriteSocket c0Out = new("c", 1);
        ReadSocket c2In = new("c", 2);
        WriteSocket f3Out = new("f", 3);
        ReadSocket cInfIn = new("c");
        WriteSocket cInfOut = new("c");
        HashSet<Socket> expectedSockets = new() { c0Out, c2In, f3Out, cInfIn, cInfOut };

        HashSet<Event> lastPremises = new()
        {
            Event.Know(new FunctionMessage("x@cell", new() { new VariableMessage("x") }))
        };

        // The socket histories, which are required for rule construction.
        Dictionary<Socket, int> c0Write1History = new() { { c0Out, 0 } };
        Dictionary<Socket, int> c0Write2History = new() { { c0Out, 1 } };
        Dictionary<Socket, int> f3Out1History = new() { { f3Out, 0 } };

        HashSet<IMutateRule> expectedMutations = new()
        {
            // Branch 0 (out(c, d); out(c, e)) rules. Note the lack of cross link rules.
            new KnowChannelContentRule(c0Out),
            new FiniteWriteRule(c0Out, c0Write1History, new(), new NameMessage("d")),
            new FiniteWriteRule(c0Out, c0Write2History, new(), new NameMessage("e")),
            new ShutSocketsRule(new Dictionary<Socket, int>() { { c0Out, 2 } }),
            // Branch 1 is the parallel composition process - nothing.
            // Branch 2 (in(c, v)) rules.
            new OpenSocketsRule(new List<Socket>() { c2In }, new List<Socket>() { c0Out }),
            new FiniteReadRule(c2In, 0, "v"),
            new AttackChannelRule("v"),
            new ShutSocketsRule(new Dictionary<Socket, int>() { { c2In, 1 } }),
            // Branch 3 (new f: channel; out(f, d)) rules.
            new KnowChannelContentRule(f3Out),
            new FiniteWriteRule(f3Out, f3Out1History, new(), new NameMessage("d")),
            new ShutSocketsRule(new Dictionary<Socket, int>() { { f3Out, 1 } }),
            // Branch 4 is the replicant process - nothing.
            // Branch 5 (in(c, x: bitstring); out(c, f)) rules.
            new OpenSocketsRule(new List<Socket>() { cInfIn, cInfOut }, new List<Socket>() { f3Out }),
            new KnowChannelContentRule(cInfOut),
            // Note that "@f@0" in the next rule is due to a channel being sent from a replicated process.
            new InfiniteWriteRule(cInfOut, lastPremises, new NameMessage("@f@0")),
            new ReadResetRule(cInfIn),
            new InfiniteReadRule(cInfIn, "x"),
            new AttackChannelRule("x"),
            new FiniteCrossLinkRule(cInfOut, c2In)
        };

        DoMutateTest(testSource, expectedSockets, expectedMutations);
    }

    [TestMethod]
    public void ProVerifStyleRulesTest()
    {
        string testSource =
@"free c: channel.
free s: bitstring [private].

process !( out(c, s) |  in(c, v: bitstring) ).";

        ReadSocket cIn = new("c");
        WriteSocket cOut = new("c");
        HashSet<Socket> expectedSockets = new() { cIn, cOut };
        HashSet<IMutateRule> expectedMutations = new()
        {
            new KnowChannelContentRule(cOut),
            new OpenSocketsRule(cIn),
            new OpenSocketsRule(cOut),
            new InfiniteCrossLink(cOut, cIn, new(), new NameMessage("s"), "v"),
            // Following rules should not be triggered during Nession construction.
            new ReadResetRule(cIn),
            new InfiniteReadRule(cIn, "v"),
            new AttackChannelRule("v")
        };
        DoMutateTest(testSource, expectedSockets, expectedMutations);
    }

    [TestMethod]
    public void BasicIfElseTest()
    {
        string testSource =
@"free c: channel.
free s: bitstring [private].
const w: bitstring.
const x: bitstring.

process 
  in(c, v: bitstring);
  if v = w && v <> x then out(c, v).";

        ReadSocket cIn = new("c", 1);
        WriteSocket cOut = new("c", 1);
        HashSet<Socket> expectedSockets = new() { cIn, cOut };
        VariableMessage vMsg = new("v");
        IfBranchConditions ifCond = new(
            new Dictionary<IAssignableMessage, IMessage>() { { vMsg, new NameMessage("w") } },
            new BucketSet<IAssignableMessage, IMessage>(vMsg, new NameMessage("x")));
        HashSet<Event> outputPremises = new()
        {
            Event.Know(new FunctionMessage("v@cell", new() { vMsg }))
        };

        HashSet<IMutateRule> expectedMutations = new()
        {
            // Branch 0 (in(c, v)).           
            new OpenSocketsRule(cIn),
            new KnowChannelContentRule(cOut),
            new FiniteReadRule(cIn, 0, "v"),
            new AttackChannelRule("v"),
            // Branch 2 (out(c, v)) -> Note that the conditional is branch 1.
            /*new KnowChannelContentRule(cOut)
            {
                Conditions = ifCond
            },*/
            new FiniteWriteRule(cOut, new Dictionary<Socket, int>() { { cOut, 0 }, { cIn, 1 } }, outputPremises, vMsg)
            {
                Conditions = ifCond
            },
            new ShutSocketsRule(new Dictionary<Socket, int>() { { cOut, 1 }, { cIn, 1 } })
            {
                Conditions = ifCond
            }
        };
        DoMutateTest(testSource, expectedSockets, expectedMutations);
    }

    [TestMethod]
    public void BasicLetTest()
    {
        string testSource =
@"free c: channel.
free a: bitstring [private].
free s: bitstring [private].

fun hash(s): bitstring.

process
  in(c, v: bitstring);
  let x: bitstring = hash(s) in
  out(c, x).
";

        ReadSocket cIn = new("c", 1);
        WriteSocket cOut = new("c", 2);
        HashSet<Socket> expectedSockets = new() { cIn, cOut };

        VariableMessage vMsg = new("v");
        VariableMessage xMsg = new("x");
        FunctionMessage emptyXMsg = new("x@cell", new() { xMsg });
        FunctionMessage hashMsg = new("hash", new() { new NameMessage("s") });
        FunctionMessage filledXMsg = new("x@cell", new() { hashMsg });

        FunctionMessage vCell = new("v@cell", new() { vMsg });
        HashSet<Event> letPremises = new() { Event.Know(vCell) };
        HashSet<Event> outPremises = new() { Event.Know(vCell), Event.Know(emptyXMsg) };

        HashSet<IMutateRule> expectedMutations = new()
        {
            // in(c, v)
            new OpenSocketsRule(cIn),
            new FiniteReadRule(cIn, 0, "v"),
            new AttackChannelRule("v"),
            new ShutSocketsRule(new Dictionary<Socket, int>() { { cIn, 1 } }),
            // let x: bitstring = hash(s) in...
            new LetSetRule("x", letPremises, new List<Socket>() { cIn }, new List<Socket>() { cOut }, IfBranchConditions.Empty, Event.Know(filledXMsg)),
            new KnowChannelContentRule(cOut),
            new FiniteWriteRule(cOut, new Dictionary<Socket, int>() { {cOut, 0} }, outPremises, xMsg),
            new ShutSocketsRule(new Dictionary<Socket, int>() { { cOut, 1 } })
        };
        DoMutateTest(testSource, expectedSockets, expectedMutations);
    }

    #region Convenience methods.

    private static void DoMutateTest(string piSource, HashSet<Socket> expectedSockets, HashSet<IMutateRule> expectedMutations)
    {
        ResolvedNetwork rn = ResolvedNetwork.From(Network.CreateFromCode(piSource));
        (HashSet<Socket> foundSockets, List<IMutateRule> foundMutations) = Translation.GenerateMutateRules(rn, new());

        try
        {
            Assert.IsTrue(foundSockets.SetEquals(expectedSockets), "Sockets do not match.");
        }
        catch (Exception)
        {
            Console.WriteLine("Expected following sockets:");
            WriteSet(expectedSockets);
            Console.WriteLine("Instead found following sockets:");
            WriteSet(foundSockets);
            throw;
        }

        HashSet<IMutateRule> foundAsSet = new(foundMutations);
        try
        {
            Assert.IsTrue(foundAsSet.SetEquals(expectedMutations), "Mutations do not match.");
        }
        catch (Exception)
        {
            Console.WriteLine("Expected following mutations:");
            WriteSet(expectedMutations);
            Console.WriteLine("Instead found following mutations:");
            WriteSet(foundAsSet);
            Console.WriteLine("========================================================");
            Console.WriteLine("Expected mutations not found were:");
            WriteSet(expectedMutations.Except(foundAsSet));
            Console.WriteLine("Unexpected mutations found were:");
            WriteSet(foundAsSet.Except(expectedMutations));
            throw;
        }

        // Final part of the test is to execute the rule conversion. The output is not tested here,
        // as it would add a large amount of fragility to the tests. The purpose is to ensure that
        // nothing causes an unexpected exception.
        RuleFactory factory = new();
        foreach (IMutateRule r in expectedMutations)
        {
            r.GenerateRule(factory);
        }
    }

    private static void WriteSet(IEnumerable<object> rules)
    {
        Console.WriteLine("  " + string.Join("\n  ", rules));
    }

    #endregion

}

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
        HashSet<IMutateRule> expectedMutations = new()
        {
            new KnowChannelContentRule(c2Out),
            new OpenReadSocketRule(c3In),
            new FiniteWriteRule(c2Out, 0, new(), new NameMessage("x")),
            new FiniteCrossLinkRule(c2Out, c3In),
            new FiniteReadRule(c3In, 0, "y"),
            new ShutRule(c2Out, 1),
            new ShutRule(c3In, 1)
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

        WriteSocket c0Out = new("c", 0);
        ReadSocket c2In = new("c", 2);
        WriteSocket f3Out = new("f", 3);
        ReadSocket cInfIn = new("c");
        WriteSocket cInfOut = new("c");
        HashSet<Socket> expectedSockets = new() { c0Out, c2In, f3Out, cInfIn, cInfOut };

        HashSet<Event> lastPremises = new()
        {
            Event.Know(new FunctionMessage("x@cell", new() { new VariableMessage("x") }))
        };

        HashSet<IMutateRule> expectedMutations = new()
        {
            // Branch 0 (out(c, d); out(c, e)) rules. Note the lack of cross link rules.
            new KnowChannelContentRule(c0Out),
            new FiniteWriteRule(c0Out, 0, new(), new NameMessage("d")),
            new FiniteWriteRule(c0Out, 1, new(), new NameMessage("e")),
            new ShutRule(c0Out, 2),
            // Branch 1 is the parallel composition process - nothing.
            // Branch 2 (in(c, v)) rules.
            new OpenReadSocketRule(c2In, new List<Socket>() { c0Out }),
            new FiniteReadRule(c2In, 0, "v"),
            new ShutRule(c2In, 1),
            // Branch 3 (new f: channel; out(f, d)) rules.
            new KnowChannelContentRule(f3Out),
            new FiniteWriteRule(f3Out, 0, new(), new NameMessage("d")),
            new ShutRule(f3Out, 1),
            // Branch 4 is the replicant process - nothing.
            // Branch 5 (in(c, x: bitstring); out(c, f)) rules.
            new OpenReadSocketRule(cInfIn, new List<Socket>() { f3Out }),
            new KnowChannelContentRule(cInfOut),
            new InfiniteWriteRule(cInfOut, lastPremises, new NameMessage("f")),
            new ReadResetRule(cInfIn),
            new InfiniteReadRule(cInfIn, "x"),
            new FiniteCrossLinkRule(cInfOut, c2In)
        };

        DoMutateTest(testSource, expectedSockets, expectedMutations);
    }

    #region Convenience methods.

    private static void DoMutateTest(string piSource, HashSet<Socket> expectedSockets, HashSet<IMutateRule> expectedMutations)
    {
        ResolvedNetwork rn = ResolvedNetwork.From(Network.CreateFromCode(piSource));
        (HashSet<Socket> foundSockets, List<IMutateRule> foundMutations) = Translation.GenerateMutateRules(rn);

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
    }

    private static void WriteSet(IEnumerable<object> rules)
    {
        Console.WriteLine("  " + string.Join("\n  ", rules));
    }

    #endregion

}

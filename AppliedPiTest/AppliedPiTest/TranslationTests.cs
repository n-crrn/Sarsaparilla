using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using AppliedPi;
using AppliedPi.Translate;
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
free v: bitstring [private].
process out(c, v) | in(c, v: bitstring).";

        // The hard-coding below is intentional. Referencing static or instance members of
        // ChannelCell could result in important changes not been noticed by a coder
        // amending the translation code.
        HashSet<State> expectedInits = new()
        {
            new State("c@1@Out", new NameMessage("_Initial")),
            new State("c@2@In", new NameMessage("_Initial"))
        };
        HashSet<Rule> expectedRules = ParseRules(
            "-[ ]-> k(c[])",
            "-[ (c@1@Out(_Initial[]), a0) ]-> <a0: c@1@Out(_Write(v))>",
            "-[ (c@2@In(_Initial[]), a0) ]-> <a0: c@2@In(_Waiting[])>",
            "-[ (c@1@Out(_Initial[]), a0), (c@1@Out(_Write(v)), a1), (c@2@In(_Waiting[]), b0) : { a0 <@ a1 } ]-> <a1: c@1@Out(_Shut[])>, <b0: c@2@In(_Read(v))>",
            "k(c[])(a0) -[ (c@1@Out(_Write(_vLatest)), a0) ]-> k(_vLatest)",
            "-[ (c@2@In(_Initial[]), a0), (c@2@In(_Waiting[]), a1), (c@2@In(_Read(_v0)), a2) : { a0 <@ a1, a1 <@ a2 } ]-> <a2: c@2@In(_Shut[])>");

        DoTest(testSource, expectedInits, expectedRules);
    }

    private static HashSet<Rule> ParseRules(params string[] ruleSrcs)
    {
        RuleParser rp = new();
        return new(from rs in ruleSrcs select rp.Parse(rs));
    }

    private static void DoTest(string piSource, HashSet<State> expectedStates, HashSet<Rule> expectedRules)
    {
        Network nw = Network.CreateFromCode(piSource);
        ResolvedNetwork rn = ResolvedNetwork.From(nw);
        Translation t = Translation.From(rn, nw);

        try
        {
            Assert.IsTrue(expectedStates.SetEquals(t.InitialStates), "Initial states do not match.");
        }
        catch (Exception)
        {
            Console.WriteLine("Expected following initial states:");
            Console.WriteLine("  " + string.Join("\n  ", expectedStates));
            Console.WriteLine("Instead found following initial states:");
            Console.WriteLine("  " + string.Join("\n  ", t.InitialStates));
            throw;
        }

        try
        {
            Assert.IsTrue(expectedRules.SetEquals(t.Rules), "Rules do not match.");
        }
        catch (Exception)
        {
            Console.WriteLine("Expected following rules:");
            Console.WriteLine("  " + string.Join("\n  ", expectedRules) + "\n");
            Console.WriteLine("Instead found following initial rules:");
            Console.WriteLine("  " + string.Join("\n  ", t.Rules) + "\n");
            Console.WriteLine("Differing rules are as follows:");
            Console.WriteLine("  " + string.Join("\n  ", expectedRules.Except(t.Rules).Union(t.Rules.Except(expectedRules))));
            throw;
        }
    }

}

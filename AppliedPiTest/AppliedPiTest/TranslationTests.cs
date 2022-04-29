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

    private static readonly IMessage InitialMessage = new NameMessage("_Initial");

    [TestMethod]
    public void TwoBranchTest()
    {
        string testSource =
@"free c: channel.
free x: bitstring [private].
process out(c, x) | in(c, y: bitstring).";

        // The hard-coding below is intentional. Referencing static or instance members of
        // ChannelCell could result in important changes not been noticed by a coder
        // amending the translation code.
        HashSet<State> expectedInits = new() { new("c@2@Out", InitialMessage) };
        HashSet<Rule> expectedRules = ParseRules(
            "-[ ]-> k(c[])",
            "-[ (c@2@Out(_Initial[]), a0) ]-> <a0: c@2@Out(_Write(x[]))>",
            "-[ (c@2@Out(_Initial[]), a0), (c@2@Out(_Write(_v0)), a1) : { a0 <@ a1 } ]-> <a1: c@2@Out(_Shut[])>",
            "k(c[])(a0) -[ (c@2@Out(_Write(_vLatest)), a0) ]-> k(_vLatest)"
        );

        DoTest(testSource, expectedInits, expectedRules);
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
        HashSet<State> expectedInits = new()
        { 
            new("c@0@Out", InitialMessage),
            new("c@Out", InitialMessage),
            new("f@3@Out", InitialMessage)
        };
        HashSet<Rule> expectedRules = ParseRules(
            // Initial public knowledge.
            "-[ ]-> k(c[])",
            // Branch 0 (initial branch) rules. Note that write rules are still generated, though
            // they will not actually lead to a write state transformation.
            "-[ (c@0@Out(_Initial[]), a0) ]-> <a0: c@0@Out(_Write(d[]))>",
            "-[ (c@0@Out(_Initial[]), a0), (c@0@Out(_Write(_v0)), a1) : { a0 <@ a1 } ]-> <a1: c@0@Out(_Write(e[]))>",
            "-[ (c@0@Out(_Initial[]), a0), (c@0@Out(_Write(_v0)), a1), (c@0@Out(_Write(_v1)), a2) : { a0 <@ a1, a1 <@ a2 } ]-> <a2: c@0@Out(_Shut[])>",
            "k(c[])(a0) -[ (c@0@Out(_Write(_vLatest)), a0) ]-> k(_vLatest)",
            // Branch 1 is the parallel composition process - nothing.
            // Branch 2 (in(c, v)) rules - nothing.
            // Branch 3 (new f: channel; out(f, d)) rules.
            "-[ (f@3@Out(_Initial[]), a0) ]-> <a0: f@3@Out(_Write(d[]))>",
            "k(f[])(a0) -[ (f@3@Out(_Write(_vLatest)), a0) ]-> k(_vLatest)",
            "-[ (f@3@Out(_Initial[]), a0), (f@3@Out(_Write(_v0)), a1) : { a0 <@ a1 } ]-> <a1: f@3@Out(_Shut[])>",
            // No further f rules generated as there are no reads on the f channel.
            // Branch 5 (! in(c, x); out(c, f)) rules.
            "k(x[])(a0) -[ (c@0@Out(_Shut[]), a0) ]-> k(f[])"
        );

        DoTest(testSource, expectedInits, expectedRules);
    }

    #region Convenience methods.

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
            WriteSet(expectedStates);
            Console.WriteLine("Instead found following initial states:");
            WriteSet(t.InitialStates);
            throw;
        }

        try
        {
            Assert.IsTrue(expectedRules.SetEquals(t.Rules), "Rules do not match.");
        }
        catch (Exception)
        {
            Console.WriteLine("Expected following rules:");
            WriteSet(expectedRules);
            Console.WriteLine("Instead found following initial rules:");
            WriteSet(t.Rules);
            Console.WriteLine("========================================================");
            Console.WriteLine("Expected rules not found were:");
            WriteSet(expectedRules.Except(t.Rules));
            Console.WriteLine("Unexpected rules found were:");
            WriteSet(t.Rules.Except(expectedRules));
            throw;
        }
    }

    private static void WriteSet(IEnumerable<object> rules)
    {
        Console.WriteLine("  " + string.Join("\n  ", rules));
    }

    #endregion

}

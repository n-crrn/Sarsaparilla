using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StatefulHorn;
using AppliedPi;
using AppliedPi.Translate;

namespace SarsaparillaTests.AppliedPiTest;

/// <summary>
/// Tests for the complete chain of Applied Pi through Stateful Horn through querying.
/// </summary>
[TestClass]
public class IntegrationTests
{

    [TestMethod]
    public async Task BasicChainTest()
    {
        string piSource =
@"free c: channel.
free s: bitstring [private].
query attacker(s).
process ( out(c, s) | in(c, v: bitstring) ).
";
        await DoTest(piSource, false, true);
    }

    [TestMethod]
    public async Task ValueTransferTest()
    {
        string piSource =
@"free c: channel.
free d: channel [private].
free s: bitstring [private].

query attacker(s).

process
  out(d, s) | ( in(d, v: bitstring); out(c, v) ).
";
        await DoTest(piSource, false, true);
    }

    [TestMethod]
    public async Task FalseAttackAvoidanceTest()
    {
        string piSource =
@"free c: channel.
free d: channel [private].
free s: bitstring [private].

query attacker(s).

process
  out(d, s) | ( in(d, v: bitstring); out(c, d) ).
";
        await DoTest(piSource, false, false);
    }

    [TestMethod]
    public async Task ReplicatedToFiniteDetectionTest()
    {
        string piSource =
@"free c: channel.
free d: channel [private].
free s: bitstring [private].

query attacker(s).

process
  (! out(d, s)) | ( in(d, v: bitstring); out(c, d) ).
";
        await DoTest(piSource, false, true);
    }

    [TestMethod]
    public async Task QueryInMacroTests()
    {
        string piSource1 =
@"free c: channel.

query attacker(b).

let macro = new b: bitstring; out(c, b).
process macro.";

        await DoTest(piSource1, false, true);

        string piSource2 =
@"free c: channel.

query attacker((b, d)).

let macro1 = new b: bitstring; out(c, b).
let macro2 = new d: bitstring; out(c, d).
process macro1 | macro2.
";
        await DoTest(piSource2, false, true);
    }

    [TestMethod]
    public async Task KnowBoolTest()
    {
        string piSource =
@"free c: channel.
free secret: bitstring [private].
free something: bitstring [private].

query attacker(secret).

process 
  ( out(c, something) |
    ( in(c, v: bitstring); if v <> something then out(c, secret) ) ).";

        // Though "something" is the value sent, the attacker can intervene and send anything else.
        await DoTest(piSource, false, true);
    }

    [TestMethod]
    public async Task DeconstructorTest()
    {
        string piSource =
@"free c: channel.

query attacker(new value).

type key.
free theKey: key.

fun enc(bitstring, bitstring): bitstring.
reduc forall x: bitstring, y: key; dec(enc(x, y), y) = x.

process 
  new value: bitstring;
  out(c, enc(value, theKey)).";
        await DoTest(piSource, false, true);
    }

    [TestMethod]
    public async Task LetTest()
    {
        string piSource =
@"free publicChannel: channel.
free value1: bitstring [private].
free value2: bitstring [private].

fun h(bitstring): bitstring.

query attacker(value1).

process
  new c: channel;
  ( out(c, (value1, value2)) | 
    ( in(c, v: bitstring); let (a: bitstring, b: bitstring) = v in out(publicChannel, a) ) ).
";
        await DoTest(piSource, false, true);
    }

    [TestMethod]
    public async Task LoopTest()
    {
        // In this first example, there is a process that is capable of generating
        // h(h(value)) but it must be run at least twice to do so. This is a test
        // of a channel being made public from its inner scope.
        string piSource1 =
@"free pubC: channel.
free value: bitstring.
const holder: bitstring.

fun h(bitstring): bitstring [private].

query attacker(h(h(value))).

process
  ( in(pubC, aChannel: channel) ) (* Read anything from public channel. *)
  | (! ( new c: channel;
         out(pubC, c);
         ( in(c, inRead: bitstring);
           out(c, h(inRead)) )
         | ( out(c, holder);
             in(c, v: bitstring) ) ) ).
";
        await DoTest(piSource1, false, true);

        // In this second example, the channel is again made public but the process
        // can only run once. Therefore, h(h(value)) cannot be generated.
        string piSource2 =
@"free pubC: channel.
free value: bitstring.
const holder: bitstring.

fun h(bitstring): bitstring [private].

query attacker(h(h(value))).

process
  ( in(pubC, aChannel: channel) ) (* Read anything from public channel. *)
  | ( new c: channel;
      out(pubC, c);
      ( in(c, inRead: bitstring);
        out(c, h(inRead)) )
      | ( out(c, holder);
          in(c, v: bitstring) ) ).
";
        await DoTest(piSource2, false, false);

        // In this final example, the channel is made public and the process is replicated.
        // However, the magic transformation occurs in its concurrent process.
        string piSource3 =
@"free pubC: channel.
free value: bitstring.
const holder: bitstring.

fun h(bitstring): bitstring [private].

query attacker(h(h(value))).

process
  in(pubC, aChannel: channel) (* Read anything from public channel. *)
  | !( new c: channel;
       ( out(pubC, c); out(c, holder); in(c, v: bitstring) )
       | ( in(c, inRead: bitstring); out(c, h(inRead)) ) )
";
        await DoTest(piSource3, false, true);
    }


    /// <summary>
    /// Conducts a full integration test, where source code is used to construct a Network to
    /// conduct a query upon. This is a public method as some other groups of tests
    /// (e.g. ThesisTests) need to exercise this functionality as well.
    /// </summary>
    /// <param name="src">Applied Pi model source code to test.</param>
    /// <param name="expectGlobalAttack">
    ///   Whether a global attack is expected to be detected.
    /// </param>
    /// <param name="expectNessionAttack">
    ///   Whether one or more nessions are expected to demonstrate an attack.
    /// </param>
    /// <returns></returns>
    public async static Task DoTest(string src, bool expectGlobalAttack, bool expectNessionAttack)
    {
        Network nw = Network.CreateFromCode(src);
        ResolvedNetwork rn = ResolvedNetwork.From(nw);
        Translation t = Translation.From(rn, nw);

        try
        {
            // Preparations for running the query engine.
            bool globalAttackFound = false;
            void onGlobalAttackFound(Attack a) => globalAttackFound = true;
            bool nessionAttackFound = false;
            void onAttackAssessedFound(Nession n, HashSet<HornClause> hs, Attack? a) => nessionAttackFound |= a != null;

            foreach (QueryEngine qe in t.QueryEngines())
            {
                await qe.Execute(null, onGlobalAttackFound, onAttackAssessedFound, null, t.RecommendedDepth);

                if (globalAttackFound && nessionAttackFound)
                {
                    break; // Mission achieved.
                }
            }
            
            Assert.AreEqual(expectGlobalAttack, globalAttackFound, "Global attack finding.");
            Assert.AreEqual(expectNessionAttack, nessionAttackFound, "Expected non-global attack.");
        }
        catch (Exception)
        {
            Console.WriteLine("Queries are:");
            foreach (IMessage q in t.Queries)
            {
                Console.WriteLine("  " + q.ToString());
            }

            Console.WriteLine("Initial states are:");
            foreach (State ini in t.InitialStates)
            {
                Console.WriteLine("  " + ini.ToString());
            }

            Console.WriteLine("Translated rules were as follows:");
            foreach (Rule r in t.Rules)
            {
                Console.WriteLine("  " + r.ToString());
            }
            throw;
        }
    }
}

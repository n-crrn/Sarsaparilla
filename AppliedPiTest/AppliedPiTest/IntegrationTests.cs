using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StatefulHorn;
using AppliedPi;
using AppliedPi.Translate;
using StatefulHorn.Query;

namespace SarsaparillaTests.AppliedPiTest;

/// <summary>
/// Tests for the complete chain of Applied Pi through Stateful Horn through querying.
/// </summary>
[TestClass]
public class IntegrationTests
{

    /// <summary>
    /// Demonstrates that a query of an already publicly known free name will work.
    /// </summary>
    /// <returns>Awaitable Task.</returns>
    [TestMethod]
    public async Task TrivialTest()
    {
        string piSource =
@"free c: channel.
free s: bitstring.
query attacker(s).
process in(c, x:bitstring) | out(c, s).
";
        await DoTest(piSource, true);
    }

    /// <summary>
    /// Demonstrates that a private value can be found if it is transmitted on a publicly known
    /// channel.
    /// </summary>
    /// <returns>Awaitable Task.</returns>
    [TestMethod]
    public async Task BasicChainTest()
    {
        string piSource =
@"free c: channel.
free s: bitstring [private].
query attacker(s).
process ( out(c, s) | in(c, v: bitstring) ).
";
        await DoTest(piSource, true);
    }

    /// <summary>
    /// Demonstrates that a name leaked indirectly can be found.
    /// </summary>
    /// <returns>Awaitable Task.</returns>
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
        await DoTest(piSource, true);
    }

    [TestMethod]
    public async Task FalseAttackAvoidanceTest()
    {
        await DoTest(ModelSampleLibrary.FalseAttackAvoidanceModelCode, false);
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
        await DoTest(piSource, true);
    }

    /// <summary>
    /// Demonstrate that a queried name that is only used wtihin a macro can still be found.
    /// </summary>
    /// <returns>Awaitable Task.</returns>
    [TestMethod]
    public async Task QueryInMacroTests()
    {
        string piSource1 =
@"free c: channel.

query attacker(b).

let macro = new b: bitstring; out(c, b).
process macro.
";
        await DoTest(piSource1, true);

        await DoTest(ModelSampleLibrary.MacroModelCode, true);
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
        await DoTest(piSource, true);
    }

    [TestMethod]
    public async Task DeconstructorTest()
    {
        await DoTest(ModelSampleLibrary.DeconstructorModelCode, true);
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
        await DoTest(piSource, true);
    }

    [TestMethod]
    public async Task LetDestructorTest()
    {
        string piSource =
@"type key.

free c: channel.
free d: channel [private].
free s: bitstring [private].
free k: key.

fun enc(bitstring, key): bitstring [private].
reduc forall x: bitstring, y: key; dec(enc(x, y), y) = x.

query attacker(s).

process
  out(d, enc(s, k)) |
  (in(d, v: bitstring); let r: bitstring = dec(v, k) in out(c, r) ).
";
        await DoTest(piSource, true);
    }

    /// <summary>
    /// Tests the leaking of values that are sent over channels that are themselves leaked.
    /// </summary>
    /// <returns>Awaitable Task.</returns>
    [TestMethod]
    public async Task ChannelLeakTest()
    {
        // In this first example, there is a process that is capable of generating
        // h(h(value)) but it must be run at least twice to do so. This is a test
        // of a channel being made public from its inner scope.
        await DoTest(ModelSampleLibrary.ChannelLeak1Code, true);

        // In this second example, the channel is again made public but the process
        // can only run once. Therefore, h(h(value)) cannot be generated.
        await DoTest(ModelSampleLibrary.ChannelLeak2Code, false); 

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
        await DoTest(piSource3, true);
    }

    [TestMethod]
    public async Task LetIfTest()
    {
        // The difference between the two models below is the if test at the end of the SD macro.
        string piSource1 =
            @"type key.

const left: bitstring.
const right: bitstring.

fun h(bitstring, bitstring): bitstring.
fun pk(key): key.
fun enc(bitstring, key): bitstring.
reduc forall x: bitstring, y: key; dec(enc(x, pk(y)), y) = x.

query attacker(s).

free publicChannel: channel.
free bobl: bitstring [private].
free bobr: bitstring [private].
free s: bitstring [private].

let SD(b: channel, sk: key) =
  new mStart: bitstring;   (* State value of the security device. *)
  in(b, x: bitstring);     (* Arbitrary value. *)
  let mUpdated: bitstring = h(mStart, x) in
  out(b, mUpdated);          (* Send state value, simulate read. *)
  in(b, match: bitstring);
  if match <> mUpdated then
    out(publicChannel, s).

process
  new b: channel;
  new k: key;
  ( SD(b, k) | 
    ( new arb: bitstring;
      out(b, arb);
      in(b, readValue: bitstring);
      out(b, readValue) ) ) 
  | in(publicChannel, w: bitstring).";
        await DoTest(piSource1, false);

        string piSource2 =
            @"type key.

const left: bitstring.
const right: bitstring.

fun h(bitstring, bitstring): bitstring.
fun pk(key): key.
fun enc(bitstring, key): bitstring.
reduc forall x: bitstring, y: key; dec(enc(x, pk(y)), y) = x.

query attacker(s).

free publicChannel: channel.
free bobl: bitstring [private].
free bobr: bitstring [private].
free s: bitstring [private].

let SD(b: channel, sk: key) =
  new mStart: bitstring;   (* State value of the security device. *)
  in(b, x: bitstring);     (* Arbitrary value. *)
  let mUpdated: bitstring = h(mStart, x) in
  out(b, mUpdated);          (* Send state value, simulate read. *)
  in(b, match: bitstring);
  if match = mUpdated then
    out(publicChannel, s).

process
  new b: channel;
  new k: key;
  ( SD(b, k) | 
    ( new arb: bitstring;
      out(b, arb);
      in(b, readValue: bitstring);
      out(b, readValue) ) ) 
  | in(publicChannel, w: bitstring).";
        await DoTest(piSource2, true);
    }

    [TestMethod]
    public async Task InfiniteWriteCorrectnessTest()
    {
        string piSource1 =
@"free c: channel.
free s: bitstring [private].

query attacker(s).

process
    new d: channel;
    (!out(d, s)) | !(in(d, v: bitstring); out(c, v)) | !in(c, w: bitstring).
";
        await DoTest(piSource1, true);
    }

    /// <summary>
    /// Ensure that two consecutive writes to a socket (after a read) will trigger correctly.
    /// </summary>
    /// <returns>Task for asynchronous execution.</returns>
    [TestMethod]
    public async Task DoubleWriteTest()
    {
        string piSource =
@"free c: channel.
free d: channel [private].

free p: bitstring.
free s: bitstring [private].

query attacker(s).

process
    (out(d, s); in(c, v: bitstring); in(c, w: bitstring)) 
  | (in(d, x: bitstring); out(c, p); out(c, x)).
";
        await DoTest(piSource, true);
    }

    /// <summary>
    /// Ensure that grouped tupled are detected.
    /// </summary>
    /// <returns></returns>
    [TestMethod]
    public async Task TupleTest()
    {
        string piSource1 =
@"free c: channel.
free s1: bitstring [private].
free s2: bitstring [private].

query attacker((s1, s2)).

process out(c, s1) | out(c, s2) | !in(c, v: bitstring).
";
        await DoTest(piSource1, true);

        string piSource2 =
@"free c: channel.
free s1: bitstring [private].
free s2: bitstring [private].
free v1: bitstring.
free v2: bitstring.

query attacker((s1, s2)).

let test =
    in(c, value: bitstring);
    if value = v1 then
        out(c, s1)
    else if value = v2 then
        out(c, s2).

process test | out(c, v1) | out(c, v2) | !in(c, s: bitstring).
";
        await DoTest(piSource2, true);

        string piSource3 =
@"free c: channel.
free s1: bitstring [private].
free s2: bitstring [private].
free v1: bitstring.
free v2: bitstring.

query attacker((s1, s2)).

let test(which_value: bitstring) = 
    in(c, value: bitstring);
    if value = v1 then
        out(c, s1)
    else if value = v2 then
        out(c, s2).

process test(v1) | test(v2) | out(c, v1) | out(c, v2) | !in(c, s: bitstring).
";
        await DoTest(piSource3, true);
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
    public async static Task DoTest(string src, bool expectNessionAttack)
    {
        Network nw = Network.CreateFromCode(src);
        ResolvedNetwork rn = ResolvedNetwork.From(nw);
        Translation t = Translation.From(rn, nw);

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

        try
        {
            // Preparations for running the query engine.
            bool nessionAttackFound = false;
            void onAttackAssessedFound(Nession n, IReadOnlySet<HornClause> hs, Attack? a) => nessionAttackFound |= a != null;

            QueryEngine qe = t.CreateQueryEngine()!;
            await qe.Execute(null, onAttackAssessedFound, null, t.RecommendedElaborationLimit);

            Assert.AreEqual(expectNessionAttack, nessionAttackFound, "Non-global attack.");
        }
        catch (Exception)
        {
            throw;
        }
    }
}

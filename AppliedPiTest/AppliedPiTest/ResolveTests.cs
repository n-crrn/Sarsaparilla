using System;
using System.Collections.Generic;

using AppliedPi.Model;
using AppliedPi.Processes;
using AppliedPi;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SarsaparillaTests.AppliedPiTest;

[TestClass]
public class ResolveTests
{

    /// <summary>
    /// Ensures that basic processes (EventProcess, InChannelProcess, OutChannelProcess, NewProcess)
    /// are correctly resolved.
    /// </summary>
    [TestMethod]
    public void BasicResolutionTest()
    {
        // Create test ResolvedNetwork first - see if it raises exceptions during creation.
        string basicSource =
        @"(* Full test of basic processes - that is, excluding CallProcess. *)
free c: channel.
type key.
event gotKey(key).

process
    new Kas: key;
    out(c, Kas);
    in(c, tv: key);
    event gotKey(tv).
";
        Network nw = Network.CreateFromCode(basicSource);
        ResolvedNetwork resNw = ResolvedNetwork.From(nw);

        // Create expected ResolvedNetwork.
        Dictionary<Term, TermOriginRecord> details = new(TermResolver.BuiltInValues)
        {
            { new("c"), new(TermSource.Free, new("channel")) },
            { new("Kas"), new(TermSource.Nonce, new("key")) },
            { new("tv"), new(TermSource.Input, new("key")) }
        };
        List<IProcess> sequence = new()
        {
            new NewProcess("Kas", "key", null),
            new OutChannelProcess("c", new("Kas"), null),
            new InChannelProcess("c", new() { ("tv", "key") }, null),
            new PiEventProcess(new("gotKey", new() { new("tv") }), null)
        };
        ResolvedNetwork expectedResNw = new();
        expectedResNw.DirectSet(details, sequence);

        // Check the network correct.
        CheckResolvedNetworks(expectedResNw, resNw);
    }

    [TestMethod]
    public void FullResolutionTest()
    {
        // Create test ResolvedNetwork first - see if it raises exceptions during creation.
        // Include a macro with no arguments to specifically ensure its variables are 
        // handled correctly.
        string source =
            @"(* Full test of calling processings. *)
free c: channel.
type key.
fun encrypt(bitstring, key): bitstring.
reduc forall x: bitstring, y: key; decrypt(encrypt(x, y),y) = x.
event gotValue(bitstring).
const Good: bitstring.

(* Sub-processes *)
let rx(k: key) = in(c, value: bitstring);
                 let x: bitstring = decrypt(value, k) in event gotValue(x).
let send(k: key) = out(c, encrypt(Good, k)).
let randomIntercept = in(c, iValue: bitstring).

(* Main *)
process
    new kValue: key;
    (! send(kValue) | ! rx(kValue) | randomIntercept).
";
        Network nw = Network.CreateFromCode(source);
        ResolvedNetwork resNw = ResolvedNetwork.From(nw);

        // Create expected ResolvedNetwork.
        Dictionary<Term, TermOriginRecord> details = new(TermResolver.BuiltInValues)
        {
            { new("c"),        new(TermSource.Free, PiType.Channel) },
            { new("Good"),     new(TermSource.Constant, PiType.BitString) },
            { new("kValue"),   new(TermSource.Nonce, new("key")) },
            { new("encrypt", new() { new("Good"), new("kValue") }),
                               new(TermSource.Constructor, PiType.BitString) },
            { new("rx@value"), new(TermSource.Input, PiType.BitString) },
            { new("rx@x"),     new(TermSource.Let, PiType.BitString) },
            { new("randomIntercept@iValue"), new(TermSource.Input, PiType.BitString) }
        };
        List<IProcess> sequence = new()
        {
            new NewProcess("kValue", "key", null),
            new ParallelCompositionProcess(
                new List<IProcess>()
                {
                    new ReplicateProcess(
                        new OutChannelProcess("c", new Term("encrypt", new() { new("Good"), new("kValue") }), null),
                        null
                    ),
                    new ReplicateProcess(
                        new ProcessGroup(
                            new List<IProcess>()
                            {
                                new InChannelProcess("c", new() { ("rx@value", "bitstring") }, null),
                                new LetProcess(
                                        TuplePattern.CreateSingle("rx@x", "bitstring"),
                                        new Term("decrypt", new() { new("rx@value"), new("kValue")}),
                                        new PiEventProcess(new("gotValue", new() { new("rx@x") }), null),
                                        null,
                                        null
                                    ),
                            },
                            null),
                        null
                    ),
                    new InChannelProcess("c", new() { ("randomIntercept@iValue", "bitstring") }, null)
                }, 
                null)
        };
        ResolvedNetwork expectedResNw = new();
        expectedResNw.DirectSet(details, sequence);

        // Check the network correct.
        CheckResolvedNetworks(expectedResNw, resNw);
    }

    [TestMethod]
    public void ChannelResolutionTest()
    {
        string source =
@"free c: channel.
const S: bitstring.

let send = new d: channel; out(c, d).
let rx = in(c, x: channel); out(x, S).
process (send | rx).";
        Network nw = Network.CreateFromCode(source);
        ResolvedNetwork resNw = ResolvedNetwork.From(nw);

        Dictionary<Term, TermOriginRecord> details = new(TermResolver.BuiltInValues)
        {
            { new("c"), new(TermSource.Free, PiType.Channel) },
            { new("S"), new(TermSource.Constant, PiType.BitString) },
            { new("send@d"), new(TermSource.Nonce, PiType.Channel) },
            { new("rx@x"), new(TermSource.Input, PiType.Channel) }
        };
        List<IProcess> sequence = new()
        {
            new ParallelCompositionProcess(
                new List<IProcess>()
                {
                    new ProcessGroup(
                        new List<IProcess>()
                        {
                            new NewProcess("send@d", Network.ChannelType, null),
                            new OutChannelProcess("c", new Term("send@d"), null)
                        },
                        null),
                    new ProcessGroup(
                        new List<IProcess>()
                        {
                            new InChannelProcess("c", new() { ("rx@x", "channel") }, null),
                            new OutChannelProcess("rx@x", new Term("S"), null)
                        },
                        null)
                },
                null)
        };
        ResolvedNetwork expectedResNw = new();
        expectedResNw.DirectSet(details, sequence);

        CheckResolvedNetworks(expectedResNw, resNw);
    }

    private static void CheckResolvedNetworks(ResolvedNetwork expected, ResolvedNetwork result)
    {
        try
        {
            Assert.AreEqual(expected, result, "Networks do not match.");
        }
        catch (Exception)
        {
            Console.WriteLine("=== Expected network resolved as follows ===");
            expected.Describe(Console.Out);
            Console.WriteLine("=== Generated network resolved as follows ===");
            result.Describe(Console.Out);
            throw;
        }
    }

}

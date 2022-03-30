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
        Dictionary<Term, (TermSource Source, string PiType)> details = new()
        {
            { new("c"), (TermSource.Free, "channel") },
            { new("Kas"), (TermSource.Nonce, "key") },
            { new("tv"), (TermSource.Input, "key") }
        };
        List<IProcess> sequence = new()
        {
            new NewProcess("Kas", "key"),
            new OutChannelProcess("c", new("Kas")),
            new InChannelProcess("c", new() { ("tv", "key") }),
            new EventProcess(new("gotKey", new() { new("tv") }))
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

(* Main *)
process
    new kValue: key;
    (! send(kValue) | ! rx(kValue)).
";
        Network nw = Network.CreateFromCode(source);
        ResolvedNetwork resNw = ResolvedNetwork.From(nw);

        // Create expected ResolvedNetwork.
        Dictionary<Term, (TermSource Source, string PiType)> details = new()
        {
            { new("c"),        (TermSource.Free, Network.ChannelType) },
            { new("Good"),     (TermSource.Constant, Network.BitstringType) },
            { new("kValue"),   (TermSource.Nonce, "key") },
            { new("rx@value"), (TermSource.Input, Network.BitstringType) },
            { new("rx@x"),     (TermSource.Let, Network.BitstringType) }
        };
        List<IProcess> sequence = new()
        {
            new NewProcess("kValue", "key"),
            new ParallelCompositionProcess(new List<IProcess>()
            {
                new ReplicateProcess(
                    new OutChannelProcess("c", new Term("encrypt", new() { new("Good"), new("kValue") }))
                ),
                new ReplicateProcess(
                    new ProcessGroup(new List<IProcess>()
                    {
                        new InChannelProcess("c", new() { ("rx@value", "bitstring") }),
                        new LetProcess(
                                TuplePattern.CreateSingle("rx@x", "bitstring"),
                                new Term("decrypt", new() { new("rx@value"), new("kValue")}),
                                new EventProcess(new("gotValue", new() { new("rx@x") }))
                            ),
                    })
                )
            })
        };
        ResolvedNetwork expectedResNW = new();
        expectedResNW.DirectSet(details, sequence);

        // Check the network correct.
        CheckResolvedNetworks(expectedResNW, resNw);
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

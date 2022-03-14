using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        string basicSource =
        @"(* Full test of basic processes - that is, excluding CallProcess. *)
free c: channel.
type key.
type nonce.
fun encrypt(bitstring, key): bitstring.
reduc forall x: bitstring, y: key; decrypt(encrypt(x, y),y) = x.
event gotKey(key).
const defaultKey: key.

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
        List<(IProcess Process, bool Replicated)> sequence = new()
        {
            (new NewProcess("Kas", "key"), false),
            (new OutChannelProcess("c", new("Kas")), false),
            (new InChannelProcess("c", new() { ("tv", "key") }), false),
            (new EventProcess(new("gotKey", new() { new("tv") })), false)
        };
        ResolvedNetwork expectedResNw = new();
        expectedResNw.DirectSet(details, sequence);

        CheckResolvedNetworks(expectedResNw, resNw);
    }

    /*
    [TestMethod]
    public void FullResolutionTest()
    {

    }
    */

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

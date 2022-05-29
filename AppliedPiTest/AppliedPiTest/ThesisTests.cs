using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SarsaparillaTests.AppliedPiTest;

/// <summary>
/// This collection of tests concern the examples used in the original Thesis for the techniques
/// used by Sarsaparilla.
/// </summary>
[TestClass]
public class ThesisTests
{

    [TestMethod]
    public async Task BobSDTest()
    {
        string piSource =
@"type key.

const left: bitstring.
const right: bitstring.

fun h(bitstring):bitstring.
fun pk(key):key.

query attacker((bobl, bobr)).

free publicChannel: channel.

let SD(b: channel, sk: key) =
  new mStart: bitstring;
  (* Configure left or right. *)
  in(b, x: bitstring); 
  (* Read instruction. *)
  out(b, mStart);
  let mUpdated: bitstring = h(mStart, x) in
  in(b, enc_rx: bitstring);
  let (mf: bitstring, sl: bitstring, sr: bitstring, =pk(sk)) = enc_rx in
    if mUpdated = h(mStart, left) then
      out(b, sl)
    else
      if mUpdated = h(mStart, right) then
        out(b, sr).
  (* Otherwise, just ignore. *)

let Bob(b: channel, sk: key) =
  new bobl: bitstring;
  new bobr: bitstring;
  (* Read from SD. *)
  in(b, mf: bitstring); 
  out(b, (mf, bobl, bobr, pk(sk)));
  in(b, result: bitstring).

let BobSDSet(which: bitstring) =
  new b: channel;
  new k: key;
  out(publicChannel, b);
  ( SD(b, k) |
    ( out(b, which); Bob(b, k) ) ).

process
  (! BobSDSet(left) |
   ! BobSDSet(right) | ! in(publicChannel, bChan: channel) ).
";
        await IntegrationTests.DoTest(piSource, false, false);
    }

}

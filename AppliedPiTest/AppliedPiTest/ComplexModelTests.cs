using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SarsaparillaTests.AppliedPiTest;

/// <summary>
/// Integration tests featuring complex models that test multiple features of the translation and
/// query systems.
/// </summary>
[TestClass]
public class ComplexModelTests
{

    /// <summary>
    /// Model that should leak the name bobl[].
    /// </summary>
    /// <returns>Awaitable Task.</returns>
    [TestMethod]
    public async Task LeakNameModelTest()
    {
        string piSource =
@"type key.

const left: bitstring.
const right: bitstring.

fun h(bitstring, bitstring): bitstring.
fun pk(key): key.
fun enc(bitstring, key): bitstring.
reduc forall x: bitstring, y: key; dec(enc(x, pk(y)), y) = x.

set maximumTerms = 20000.
query attacker(bobl).

free publicChannel: channel.
free bobl: bitstring [private].
free bobr: bitstring [private].

let SD(b: channel, sk: key) =
  new mStart: bitstring;   (* State value of the security device. *)
  in(b, x: bitstring);     (* Arbitrary value. *)
  let mUpdated: bitstring = h(mStart, x) in
  out(b, mUpdated);        (* Send state value, simulate read. *)
  in(b, enc_rx: bitstring);
  let (m_f: bitstring, s_l: bitstring, s_r: bitstring) = dec(enc_rx, sk) in
  if m_f = h(mUpdated, left) then
    out(b, s_l)
  else
    if m_f = h(mUpdated, right) then
      out(b, s_r).

process
  new b: channel;
  new k: key;
  ( SD(b, k) | 
    ( new arb: bitstring;
      out(b, arb);
      in(b, readValue: bitstring);
      out(b, enc((h(readValue, left), bobl, bobr), pk(k)));
      in(b, v: bitstring);
      out(publicChannel, v) ) ) | in(publicChannel, w: bitstring).";
        await IntegrationTests.DoTest(piSource, true);
    }

    /// <summary>
    /// Model that should leak both bobl[] and bobr[] in the same session.
    /// </summary>
    /// <returns>Awaitable Task.</returns>
    [TestMethod]
    public async Task LeakTupleNameModelTest()
    {
        string piSource =
@"type key.

const left: bitstring.
const right: bitstring.

fun h(bitstring, bitstring): bitstring.
fun pk(key): key.
fun enc(bitstring, key): bitstring.
reduc forall x: bitstring, y: key; dec(enc(x, pk(y)), y) = x.

free publicChannel: channel.
free bobl: bitstring [private].
free bobr: bitstring [private].

set maximumTerms = 12000.
query attacker((bobl, bobr)).

let SD(b: channel, sk: key) =
    new mStart: bitstring;   (* State value of the security device. *)
    in(b, x: bitstring);     (* Arbitrary value. *)
    let mUpdated: bitstring = h(mStart, x) in
    out(b, mUpdated);        (* Send state value, simulate read. *)
    in(b, enc_rx: bitstring);
    let (m_f: bitstring, s_l: bitstring, s_r: bitstring) = dec(enc_rx, sk) in
    if m_f = h(mUpdated, left) then
        out(b, s_l)
    else
        if m_f = h(mUpdated, right) then
            out(b, s_r).

let Bob(left_or_right: bitstring) =
    new b: channel;
    new k: key;
    out(publicChannel, b);
    ( SD(b, k) 
      | ( new arb: bitstring;
          out(b, arb);
          in(b, readValue: bitstring);
          out(b, enc((h(readValue, left_or_right), bobl, bobr), pk(k)));
          in(b, v: bitstring) ) ).

process
  Bob(left) 
  | Bob(right) 
  | !in(publicChannel, w: bitstring).
";
        await IntegrationTests.DoTest(piSource, true);
    }

    [TestMethod]
    public async Task DirectLeakNameModelTest()
    {
        string piSource =
@"type key.

const left: bitstring.
const right: bitstring.

fun h(bitstring, bitstring): bitstring.
fun pk(key): key.
fun enc(bitstring, key): bitstring.
reduc forall x: bitstring, y: key; dec(enc(x, pk(y)), y) = x.

free publicChannel: channel.
free bobl: bitstring [private].
free bobr: bitstring [private].

set maximumTerms = 30000.
query attacker(bobl).

let SD(b: channel, sk: key) =
    new mStart: bitstring;   (* State value of the security device. *)
    in(b, x: bitstring);     (* Arbitrary value. *)
    let mUpdated: bitstring = h(mStart, x) in
    out(b, mUpdated);        (* Send state value, simulate read. *)
    in(b, enc_rx: bitstring);
    let (m_f: bitstring, s_l: bitstring, s_r: bitstring) = dec(enc_rx, sk) in
    if m_f = h(mUpdated, left) then
      out(b, s_l)
    else
      if m_f = h(mUpdated, right) then
        out(b, s_r).

let Bob(left_or_right: bitstring) =
    new b: channel;
    new k: key;
    ( SD(b, k) 
      | ( new arb: bitstring;
          out(b, arb);
          in(b, readValue: bitstring);
          out(b, enc((h(readValue, left_or_right), bobl, bobr), pk(k)));
          in(b, v: bitstring);
          out(publicChannel, v) ) ).

process
    Bob(left) |
    !in(publicChannel, w: bitstring).
";
        await IntegrationTests.DoTest(piSource, true);
    }

}

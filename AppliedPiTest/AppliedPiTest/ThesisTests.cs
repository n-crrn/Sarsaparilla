﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using AppliedPi;
using AppliedPi.Translate;

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

let SD(b: channel, sk: key) =
  new mStart: bitstring;
  (* Configure left or right. *)
  in(b, x: bitstring); 
  (* Read instruction. *)
  out(b, mStart);
  let mUpdated = h(mStart, x) in
  in(b, enc_rx: bitstring);
  let (mf, sl, sr, =pk(sk)) = enc_rx in
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
  ( SD(b, k) |
    ( out(b, which); Bob(b, k) ) ).

process
  (! BobSDSet(left) |
   ! BobSDSet(right) ).
";
        await IntegrationTests.DoTest(piSource, false, false);
    }

}
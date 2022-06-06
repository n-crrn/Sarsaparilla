using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppliedPi;

public class ModelSampleLibrary
{

    #region Properties and methods required to create and access library.

    public static readonly List<(string Title, string Description, string Sample)> Models = new();

    private static void GenerateLibrary(params (string, string)[] symbolNamesDesc)
    {
        Type ksl = typeof(ModelSampleLibrary);
        foreach ((string name, string desc) in symbolNamesDesc)
        {
            Models.Add((name, desc, (string)ksl.GetField(name)!.GetValue(null)!));
        }
    }

    static ModelSampleLibrary()
    {
        GenerateLibrary(
            ("BobSDNoAttack", "A full model demonstrating a security device."),
            ("FalseAttackAvoidanceModelCode", "A model demonstrating false attack avoidance."),
            ("DeconstructorModelCode", "A model demonstrating usage of a deconstructor."),
            ("MacroModelCode", "A demonstration of a query looking for 'new' values in macros.")
        );
    }

    #endregion
    #region The Library - Demonstration

    public const string BobSDNoAttack =
@"(*
 * Encoding of the NonceTupleCheckSampleCode sample in Applied Pi. In this
 * example, the values of bobl and bobr should not be - together -
 * determinable.
 * 
 * The original example comes from the paper 'A Verification Framework for
 * Stateful Security Protocols' in Formal Methods and Software Engineering
 * 2017 pp 262-280 by L. Li, N. Dong, J. Pang, J. Sun, G. Bai, Y. Liu and 
 * J.S. Dong.
 *)

type key.

const left: bitstring.
const right: bitstring.

fun h(bitstring): bitstring.
fun pk(key): key.
fun enc(bitstring, key): bitstring.
reduc forall x: bitstring, y: key; dec(enc(x, pk(y)), y) = x.

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
  let (mf: bitstring, sl: bitstring, sr: bitstring) = dec(enc_rx, sk) in
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
  out(b, enc((mf, bobl, bobr), pk(sk)));
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

    #endregion
    #region The Library - Functional Samples

    public const string FalseAttackAvoidanceModelCode =
@"(*
 * False attack avoidance test. In this model, the value 's' is sent over
 * channel 'c' while it is private. That channel is then made public. There
 * is no way by which the channel can be made public before the value 's' is 
 * received.
 *
 * This model was originally specified in 'Modeling and Verifying Security
 * Protocols with the Applied Pi Calculus and ProVerif' in Foundations and 
 * Trends in Privacy and Security (2016) by B. Blanchet.
 *)

free c: channel.
free d: channel [private].
free s: bitstring [private].

query attacker(s).

process
  out(d, s) | ( in(d, v: bitstring); out(c, d) ).
";

    public const string DeconstructorModelCode =
@"(*
 * This model demonstrates that Sarsaparilla can detect an attack that needs
 * a deconstructor to detect.
 *)

free c: channel.

query attacker(new value).

type key.
free theKey: key.

fun enc(bitstring, bitstring): bitstring.
reduc forall x: bitstring, y: key; dec(enc(x, y), y) = x.

process 
  new value: bitstring;
  out(c, enc(value, theKey)).
";

    public const string MacroModelCode =
@"(*
 * This model is a simple demonstration the ability of the system to handle
 * the detection of leaks from within macros.
 *)

free c: channel.

query attacker((b, d)).

let macro1 = new b: bitstring; out(c, b).
let macro2 = new d: bitstring; out(c, d).
process macro1 | macro2.";

    #endregion


}

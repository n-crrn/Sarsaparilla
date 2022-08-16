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
            ("FalseAttackAvoidanceModelCode", "A model demonstrating false attack avoidance."),
            ("ChannelLeak1Code", "A demonstration of the channel-leak rules."),
            ("ChannelLeak2Code", "A demonstration of no channel-leak from non-replicated processes."),
            ("DeconstructorModelCode", "A model demonstrating usage of a deconstructor."),
            ("MacroModelCode", "A demonstration of a query looking for 'new' values in macros."),
            ("LeakTupleNameModelCode", "A demonstration of a leak of two global values.")
        );
    }

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

    public const string ChannelLeak1Code =
@"(* 
 * Demonstration of the application of the channel-leak rules. The only way
 * that h(h(value)) can be leaked is if the second sub-process runs 
 * multiple times.
 *)

free pubC: channel.
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

    public const string ChannelLeak2Code =
@"(*
 * Demonstration of how a the leak demonstrated in ChannelLeak1Code is not
 * present in the non-replicated version of the model.
 *)
free pubC: channel.
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
    #region The Library - Full Model Samples

    public const string LeakTupleNameModelCode =
@"(*
 * Demonstration of how the simultaneous leak of two values may be found. In
 * this example, the names bobl and bobr are constant between all interactions,
 * and so may be simultaneously determined. Note that making the channel b 
 * public rather than specifically leaked messages helps to minimise the value
 * of maximumTerms used.
 *
 * This model is based on the sample problem in 'A Verification Framework for 
 * Stateful Security Protocols' in Formal Methods and Software Engineering 
 * (Springer International Publishing) 2017 by L Li, N Dong, J Pang, J Sun, 
 * G Bai, Y Liu and J.S. Dong.
 *)

type key.

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
    new b: channel;        (* Each interaction has a channel. *)
    new k: key;            (* Each interaction has its own key. *)
    out(publicChannel, b); (* Every interaction is publicly accessible. *)
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

    #endregion

}

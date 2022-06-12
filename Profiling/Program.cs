/*
 * At the time of its writing, this program was used for profiling the StatefulHorn library. This is
 * because it was not possible to do the profiling using Blazor WebAssembly, and I have made the
 * assumption that the time spent in functions in native DotNET code is comparable to that in 
 * DotNET WebAssembly.
 */
using StatefulHorn;
using AppliedPi;
using AppliedPi.Translate;

Console.WriteLine("Profiling commenced...");

/*string piSource = @"free c: channel.

query attacker(value).

const good: bitstring.
const bad: bitstring.

type key.

free k: key.

fun enc(x, y): bitstring.
reduc forall x: bitstring, y: key; dec(enc(x, y), y) = x.

process
  ( ! ( new value: bitstring;
    out(c, enc(value, k))
  ) ) | ( ! (
    in (c, rec: bitstring);
let(v: bitstring, possK: key) = rec in
    if possK = k then
      out(c, good)
    else
      out(c, bad)
  ) )";*/

/*
string piSource =
@"free c: channel.
free d: channel [private].
free s: bitstring [private].

query attacker(s).

process
  out(d, s) | ( in(d, v: bitstring); out(c, d) ).
";
*/
/*
string piSource =
@"free publicChannel: channel.
free value: bitstring.
const holder: bitstring.

fun h(bitstring): bitstring [private].

query attacker(h(h(value))).

process
  ( in(publicChannel, aChannel: channel) ) (* Read anything from public channel. *)
  | (! ( new c: channel;
         out(publicChannel, c);
         ( in(c, inRead: bitstring);
           out(c, h(inRead)) )
         | ( out(c, holder);
             in(c, v: bitstring) ) ) ).
";*/

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

Network nw = Network.CreateFromCode(piSource);
ResolvedNetwork rn = ResolvedNetwork.From(nw);
Translation t = Translation.From(rn, nw);

// --- Executing the query ---

QueryEngine qe2 = t.QueryEngines().First();

void onAttackAssessed(Nession n, IReadOnlySet<HornClause> _, Attack? a)
{
    if (a != null)
    {
        Console.WriteLine("Attack found");
    }
    /*if (a == null)
    {
        Console.WriteLine("Assessed following nession, attack NOT found.");
        Console.WriteLine(n.ToString());
    }
    else
    {
        Console.WriteLine("Attack found in following nession:");
        Console.WriteLine(n.ToString());
        Console.WriteLine("Attack details as follows:");
        Console.WriteLine(a.DescribeSources());
    }
    Console.WriteLine("----------------------------------------------");*/
}

Console.WriteLine("Following rules found...");
foreach (Rule r in t.Rules)
{
    Console.WriteLine(r);
}
Console.WriteLine("Commencing execution...");
await qe2.Execute(null, onAttackAssessed, null);
Console.WriteLine("Finished execution.");

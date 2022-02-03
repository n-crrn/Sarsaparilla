/*
 * At the time of its writing, this program was used for profiling the StatefulHorn library. This is
 * because it was not possible to do the profiling using Blazor WebAssembly, and I have made the
 * assumption that the time spent in functions in native DotNET code is comparable to that in 
 * DotNET WebAssembly.
 */
using StatefulHorn;

Console.WriteLine("Profiling commenced...");

/*string[] ruleSet =
{
    "(1) = k(sk) -[ ]-> k(pk(sk))",
    "(2) = k(m), k(pub) -[ ]-> k(aenc(m, pub))",
    "(3) = k(sk), k(aenc(m, pk(sk))) -[ ]-> k(m)",
    "(4) = k(p), k(n) -[ ]-> k(h(p, n))",
    "(9) = -[ ]-> k(left[])",
    "(10) = -[ ]-> k(right[])",
    "(11) = -[ ]-> k(init[])",
    "(12) = k(mf) -[ ]-> k(aenc(<mf, bob_l[], bob_r[]>, pk(sksd[])))",
    "(13) = k(aenc(<mf, sl, sr>, pk(sksd[])))(a1) -[ (SD(init[]), a0), (SD(h(mf, left[])), a1) : { a0 =< a1} ]-> k(sl)",
    //"(13a) = k(aenc(<mf, sl, sr>, pk(sksd[]))) -[ ]-> k(sl)",
    "(14) = k(aenc(<mf, sl, sr>, pk(sksd[])))(b1) -[ (SD(init[]), b0), (SD(h(mf, right[])), b1) : { b0 =< b1} ]-> k(sr)",
    //"(14a) = k(aenc(<mf, sl, sr>, pk(sksd[]))) -[ ]-> k(sr)",
    "(15) = -[ (SD(init[]), c0), (SD(m), c1) : { c0 =< c1} ]-> k(m)",
    "(16) = k(x)(d1) -[ (SD(init[]), d0), (SD(m), d1) : { d0 =< d1} ]-> <d1: SD(h(m, x))>"
};*/
// The following is updated to improve support for the new resolver.
string[] ruleSet =
{
    // Globally known facts.
    "-[]-> k(left[])",
    "-[]-> k(right[])",
    "-[]-> k(init[])",
    // Global derived knowledge.
    "k(m), k(pub) -[]-> k(enc_a(m, pub))",
    "k(enc_a(m, pk(sk))), k(sk) -[]-> k(m)",
    "k(sk) -[]-> k(pk(sk))",
    "k(p), k(n) -[ ]-> k(h(p, n))",
    // Session commencement and state transitions - should '_' be added as a variable/message stand-in?
    "n([bobl])(a0), n([bobr])(a0), k(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k([bobl])",
    "n([bobl])(a0), n([bobr])(a0), k(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k([bobr])",
    "k(x)(a0) -[(SD(m), a0)]-> <a0: SD(h(m, x))>",
    // Reading from states and inputs.
    //"k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k(sl)",
    //"k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k(sr)",
    "-[ (SD(init[]), a0), (SD(m), a1) : {a0 =< a1} ]-> k(m)"
};

RuleParser parser = new();
Console.WriteLine("Parsing rules...");
List<Rule> rules = new(from r in ruleSet select parser.Parse(r));

IMessage query = MessageParser.ParseMessage("[bobl]");
QueryEngine qe = new(query, rules);

List<StateConsistentRule> matches = qe.MatchingRules;
if (matches.Count > 0)
{
    Console.WriteLine("--- Immediate match(es) found: ---");
    foreach (StateConsistentRule m in matches)
    {
        Console.WriteLine(m);
    }
}
else
{
    Console.WriteLine("No initial match found (as expected).");
}

qe.Elaborate();

matches = qe.MatchingRules;
if (matches.Count > 0)
{
    Console.WriteLine("--- Match(es) found after elaboration: ---");
    foreach (StateConsistentRule m in matches)
    {
        Console.WriteLine(m);
    }
}
else
{
    Console.WriteLine("--- No matches found. ---");
}

Console.WriteLine("--- Found elaborations are as follows: ---");
foreach (Rule r in qe.Rules())
{
    Console.WriteLine(r);
}
Console.WriteLine("--- Facts are: ---");
foreach (IMessage fact in qe.Facts)
{
    Console.WriteLine(fact);
}
Console.WriteLine("--- End ---");

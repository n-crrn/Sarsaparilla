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
    "n([bobl], l[])(a0), n([bobr], l[])(a0), k(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k([bobl])",
    "n([bobl], l[])(a0), n([bobr], l[])(a0), k(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k([bobr])",
    //"(12) = k(mf) -[ ]-> k(aenc(<mf, bob_l[], bob_r[]>, pk(sksd[])))",
    //"(13) = k(aenc(<mf, sl, sr>, pk(sksd[])))(a1) -[ (SD(init[]), a0), (SD(h(mf, left[])), a1) : { a0 =< a1} ]-> k(sl)",
    "(13a) = k(aenc(<mf, sl, sr>, pk(sksd[])))(a1) -[ (SD(h(mf, left[])), a1) ]-> k(sl)",
    //"(14) = k(aenc(<mf, sl, sr>, pk(sksd[])))(b1) -[ (SD(init[]), b0), (SD(h(mf, right[])), b1) : { b0 =< b1} ]-> k(sr)",
    "(14a) = k(aenc(<mf, sl, sr>, pk(sksd[])))(a1) -[ (SD(h(mf, right[])), a1) ]-> k(sr)",
    "(15) = -[ (SD(m), c1) ]-> k(m)",
    "(16) = k(x)(d1) -[ (SD(m), d1) ]-> <d1: SD(h(m, x))>"
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
    "n([bobl], l[])(a0), n([bobr], l[])(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k([bobl])",
    "n([bobl], l[])(a0), n([bobr], l[])(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k([bobr])",
    //"n([bobl], l[]), n([bobr], r[]), k(mf) -[ ]-> k(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))",
    "k(x)(a0) -[(SD(m), a0)]-> <a0: SD(h(m, x))>",
    //"-[ (SD(m), a0) ]-> <a0: SD(init[])>",
    // Reading from states and inputs.
    "k(a), k(b), k(c) -[ ]-> k(enc_a(<a, b, c>, pk(sksd[])))",
    "k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k(sl)",
    "k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k(sr)",
    "-[ (SD(m), a0) ]-> k(m)"
};
/*string[] ruleSet =
{
    // Globally known facts.
    "-[]-> k(left[])",
    "-[]-> k(right[])",
    "-[]-> k(init[])",
    "k(p), k(n) -[ ]-> k(h(p, n))",
    "n([bobl], l[]), n([bobr], r[]), k(mf) -[ ]-> k(enc(<mf, [bobl], [bobr]>, pk(sksd[])))",
    "k(x)(a0) -[(SD(m), a0)]-> <a0: SD(h(m, x))>",
    //"-[ (SD(m), a0) ]-> <a0: SD(init[])>",
    // Reading from states and inputs.
    "k(enc(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k(sl)",
    "k(enc(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k(sr)",
};*/

// --- Rule pre-processing ---

RuleParser parser = new();
Console.WriteLine("Parsing rules...");
List<Rule> rules = new(from r in ruleSet select parser.Parse(r));

// --- Query parameters (what and when) ---

IMessage query = MessageParser.ParseMessage("<[bobl], [bobr]>");
//IMessage query = MessageParser.ParseMessage("<bob_l[], bob_r[]>");
//IMessage query = MessageParser.ParseMessage("[bobr]");
//IMessage query = MessageParser.ParseMessage("h(init[], left[])");
//IMessage query = MessageParser.ParseMessage("sksd[]");
//State when = MessageParser.ParseState("SD(h(m, right[]))");
State? when = null;
HashSet<State> initStates = new() { MessageParser.ParseState("SD(init[])") };

// --- Executing the query ---

QueryEngine qe2 = new(initStates, query, when, rules);

void onGlobalAttackFound(Attack a)
{
    Console.WriteLine("Global attack found");
    a.DescribeSources();
}

void onAttackAssessed(Nession n, HashSet<HornClause> _, Attack? a)
{
    if (a == null)
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
    Console.WriteLine("----------------------------------------------");
}

Console.WriteLine("Commencing execution...");
qe2.Execute(null, onGlobalAttackFound, onAttackAssessed, null);
Console.WriteLine("Finished execution.");

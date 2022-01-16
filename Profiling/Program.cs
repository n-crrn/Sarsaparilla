/*
 * At the time of its writing, this program was used for profiling the StatefulHorn library. This is
 * because it was not possible to do the profiling using Blazor WebAssembly, and I have made the
 * assumption that the time spent in functions in native DotNET code is comparable to that in 
 * DotNET WebAssembly.
 */
using StatefulHorn;

Console.WriteLine("Profiling commenced...");

string[] ruleSet =
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
    "(14) = k(aenc(< mf, sl, sr >, pk(sksd[])))(b1) -[(SD(init[]), b0), (SD(h(mf, right[])), b1) : { b0 =< b1} ]-> k(sr)",
    "(15) = -[(SD(init[]), c0), (SD(m), c1) : { c0 =< c1} ]-> k(m)",
    "(16) = k(x)(d1) -[(SD(init[]), d0), (SD(m), d1) : { d0 =< d1} ]-> <d1: SD(h(m, x))>",
    "(17) = k(bob_l[]), k(bob_r[]) -[ ]->leak(< bob_l[], bob_r[] >)"
};

RuleParser parser = new();
Console.WriteLine("Parsing rules...");
List<Rule> rules = new(from r in ruleSet select parser.Parse(r));

Universe uni = new(rules);
Universe.StatusReporter reporter = new(
    () => Console.WriteLine("--- Starting elaboration ---"),
    (Universe.Status s) => Console.WriteLine(s.ToString()),
    () => Console.WriteLine("--- Finished elaboration ---")
);

Console.WriteLine("Rules compiled, starting elaboration");
_ = await uni.GenerateNextRuleSet(reporter);
Console.WriteLine("--- Middle ---");
_ = await uni.GenerateNextRuleSet(reporter);
Console.WriteLine("Profiling program complete.");

Console.WriteLine("=== Change Output ===");

for (int i = 0; i < uni.ChangeLog.Count; i++)
{
    Console.WriteLine("// --- Elaboration {i} ---");
    foreach (Universe.ChangeLogEntry entry in uni.ChangeLog[i])
    {
        switch (entry.Decision)
        {
            case Universe.AddDecision.IsNew:
                Console.WriteLine($"NEW: {entry.AttemptedRule}\n");
                break;
            case Universe.AddDecision.IsImplied:
                Console.WriteLine($"IMPLIED: {entry.AttemptedRule}\n");
                Console.WriteLine("    …was instead implied by…\n");
                Console.WriteLine($"    {entry.AffectedRules![0]}\n");
                break;
            case Universe.AddDecision.ImpliesAnother:
            default:
                Console.WriteLine($"IMPLIES OTHER: {entry.AttemptedRule}\n");
                Console.WriteLine("    …implied, and therefore replaced, …\n");
                foreach (Rule affected in entry.AffectedRules!)
                {
                    Console.WriteLine($"    {affected}\n");
                }
                break;
        }
    }
}

Console.WriteLine("=== Final Ruleset ===");
foreach (StateConsistentRule scr in uni.ConsistentRules)
{
    Console.WriteLine(scr.ToString());
}
Console.WriteLine("--- Transferring Rules ---");
foreach (StateTransferringRule str in uni.TransferringRules)
{
    Console.WriteLine(str.ToString());
}

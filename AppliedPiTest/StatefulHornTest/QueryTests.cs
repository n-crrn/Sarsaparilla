using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StatefulHorn;

namespace StatefulHornTest;

[TestClass]
public class QueryTests
{

    private readonly RuleParser Parser = new();

    private async Task DoTest(
        IEnumerable<string> ruleSrcs, 
        string querySrc, 
        string stateInitSrc, 
        bool shouldFindNession, 
        bool shouldFindGlobal = false)
    {
        List<Rule> rules = new(from r in ruleSrcs select Parser.Parse(r));
        IMessage query = MessageParser.ParseMessage(querySrc);
        State initState = MessageParser.ParseState(stateInitSrc);

        QueryEngine qe = new(new HashSet<State>() { initState }, query, null, rules);
        bool globalAttackFound = false;
        bool attackAssessedFound = false;
        bool completedDone = false;

        void onGlobalAttackFound(Attack a) => globalAttackFound = true;
        void onAttackAssessedFound(Nession n, IReadOnlySet<HornClause> _, Attack? a) => attackAssessedFound |= a != null;
        void onCompletion() => completedDone = true;

        // Note that the finish check is done iteratively to speed up the test.
        await qe.Execute(null, onGlobalAttackFound, onAttackAssessedFound, onCompletion, QueryEngine.UseDefaultDepth, true);

        Assert.AreEqual(shouldFindGlobal, globalAttackFound);
        Assert.AreEqual(shouldFindNession, attackAssessedFound);
        Assert.IsTrue(completedDone, "Completion not called");
    }

    [TestMethod]
    public async Task NameTupleCheck()
    {
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
            "(13) = k(aenc(<mf, sl, sr>, pk(sksd[])))(a1) -[ (SD(h(mf, left[])), a1) ]-> k(sl)",
            "(14) = k(aenc(<mf, sl, sr>, pk(sksd[])))(a1) -[ (SD(h(mf, right[])), a1) ]-> k(sr)",
            "(15) = -[ (SD(m), c1) ]-> k(m)",
            "(16) = k(x)(d1) -[ (SD(m), d1) ]-> <d1: SD(h(m, x))>"
        };
        await DoTest(ruleSet, "<bob_l[], bob_r[]>", "SD(init[])", true, false);
    }

    [TestMethod]
    public async Task NonceChecks()
    {
        List<string> ruleSet = new()
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
            //"n([bobl], l[]), n([bobr], r[]), k(mf) -[ ]-> k(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))",
            "n([bobl])(a0), n([bobr])(a0), k(mf)(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k([bobl])",
            "n([bobl])(a0), n([bobr])(a0), k(mf)(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k([bobr])",
            "k(x)(a0) -[(SD(m), a0)]-> <a0: SD(h(m, x))>",
            // Reading from states and inputs.
            "k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k(sl)",
            "k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k(sr)",
            "-[ (SD(m), a0) ]-> k(m)"
        };

        await DoTest(ruleSet, "h(init[], left[])", "SD(init[])", false, true);
        await DoTest(ruleSet, "<[bobl], [bobr]>", "SD(init[])", false);

        ruleSet.Add("-[ (SD(m), a0) ]-> <a0: SD(init[])>");
        await DoTest(ruleSet, "<[bobl], [bobr]>", "SD(init[])", true);
    }

    [TestMethod]
    public async Task GlobalQueryCheck()
    {
        List<string> ruleSet = new()
        {
            "-[ ]-> k(n[])",
            "k(x) -[ ]-> k(test(x))"
        };
        await DoTest(ruleSet, "test(n[])", "SD(init[])", false, true);
    }

    [TestMethod]
    public async Task PureNameQueryCheck()
    {
        List<string> ruleSet = new()
        {
            "-[ ]-> k(c[])",
            "k(c[]) -[ ]-> k(d[])",
            "k(d[]) -[ ]-> k(s[])"
        };
        await DoTest(ruleSet, "s[]", "SD(init[])", false, true);
    }

    [TestMethod]
    public async Task GuardedQueriesCheck()
    {
        string initState = "SD(init[])";

        List<string> ruleSet = new()
        {
            "[x ~/> a[]] k(x), k(y) -[ ]-> k(enc(x, y))",
            "-[ ]-> k(a[])",
            "-[ ]-> k(b[])"
        };
        await DoTest(ruleSet, "enc(a[], b[])", initState, false, false);
        await DoTest(ruleSet, "enc(b[], a[])", initState, false, true);

        List<string> ruleSet2 = new()
        {
            "-[ ]-> k(test1[])",
            "-[ ]-> k(test2[])",
            "[x ~/> test1[]] k(x)(a0) -[ (SD(init[]), a0) ]-> <a0: SD(x)>",
            "-[ (SD(m), a0) ]-> k(h(m))"
        };
        await DoTest(ruleSet2, "h(test1[])", initState, false, false);
        await DoTest(ruleSet2, "h(test2[])", initState, true, false);
    }

    [TestMethod]
    public async Task DestructorCheck()
    {
        string initState = "SD(init[])";
        List<string> ruleSet = new()
        {
            "k(enc(x, pk(y))), k(y) -[ ]-> k(dec(enc(x, pk(y)), y))",
            "k(dec(enc(x, pk(y)), y)) -[ ]-> k(x)",
            "-[ ]-> k(enc(something[], pk(unknownKey[])))",
            "-[ ]-> k(enc(something[], pk(key[])))",
            "-[ ]-> k(key[])"
        };
        await DoTest(ruleSet, "something[]", initState, false, true);
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StatefulHorn;

namespace SarsaparillaTests.StatefulHornTest;

[TestClass]
public class QueryTests
{

    private readonly RuleParser Parser = new();

    private void DoTest(string[] ruleSrcs, string querySrc, string stateInitSrc, bool shouldFind, bool shouldFindGlobal = false)
    {
        List<Rule> rules = new(from r in ruleSrcs select Parser.Parse(r));
        IMessage query = MessageParser.ParseMessage(querySrc);
        State initState = MessageParser.ParseState(stateInitSrc);

        QueryEngine qe = new(new HashSet<State>() { initState }, query, null, rules);
        bool nessionsGeneratedDone = false;
        bool globalAttackFound = false;
        bool attackAssessedFound = false;
        bool completedDone = false;

        void onNessionsGenerated(NessionManager _) => nessionsGeneratedDone = true;
        void onGlobalAttackFound(Attack a) => globalAttackFound = true;
        void onAttackAssessedFound(Nession n, HashSet<HornClause> _, Attack? a) => attackAssessedFound |= a != null;
        void onCompletion() => completedDone = true;

        qe.Execute(onNessionsGenerated, onGlobalAttackFound, onAttackAssessedFound, onCompletion);
        if (shouldFindGlobal)
        {
            Assert.IsTrue(globalAttackFound, "Global attack not found when it was expected.");
        }
        else
        {
            Assert.IsTrue(nessionsGeneratedDone, "onNessionsGenerated not called.");
        }
        
        Assert.AreEqual(shouldFind, attackAssessedFound);
        Assert.IsTrue(completedDone, "Completion not called.");
    }

    [TestMethod]
    public void NameTupleCheck()
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
        DoTest(ruleSet, "<bob_l[], bob_r[]>", "SD(init[])", true);
    }

    [TestMethod]
    public void NonceChecks()
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
            "n([bobl], l[])(a0), n([bobr], l[])(a0), k(mf)(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k([bobl])",
            "n([bobl], l[])(a0), n([bobr], l[])(a0), k(mf)(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k([bobr])",
            "k(x)(a0) -[(SD(m), a0)]-> <a0: SD(h(m, x))>",
            // Reading from states and inputs.
            "k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k(sl)",
            "k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k(sr)",
            "-[ (SD(m), a0) ]-> k(m)"
        };

        /*List<string> ruleSet = new()
        {
            // Globally known facts.
            "-[]-> k(left[])",
            "-[]-> k(right[])",
            "-[]-> k(init[])",
            "n([bobl], l[]), n([bobr], r[]), k(mf) -[ ]-> k(enc(<mf, [bobl], [bobr]>, pk(sksd[])))",
            "k(x)(a0) -[(SD(m), a0)]-> <a0: SD(h(m, x))>",
            // Reading from states and inputs.
            "k(enc(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k(sl)",
            "k(enc(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k(sr)",
        };*/

        DoTest(ruleSet.ToArray(), "h(init[], left[])", "SD(init[])", true);
        DoTest(ruleSet.ToArray(), "<[bobl], [bobr]>", "SD(init[])", false);

        ruleSet.Add("-[ (SD(m), a0) ]-> <a0: SD(init[])>");
        DoTest(ruleSet.ToArray(), "<[bobl], [bobr]>", "SD(init[])", true);
    }
}

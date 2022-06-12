using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StatefulHorn;
using StatefulHorn.Messages;

namespace StatefulHornTest;

[TestClass]
public class QueryTests
{

    private readonly RuleParser Parser = new();

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
        await DoTest(ruleSet, "<bob_l[], bob_r[]>", "SD(init[])", true);//, false);
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
            // Session commencement and state transitions.
            //"n([bobl], l[]), n([bobr], r[]), k(mf) -[ ]-> k(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))",
            "n([bobl])(a0), n([bobr])(a0), k(mf)(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k([bobl])",
            "n([bobl])(a0), n([bobr])(a0), k(mf)(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k([bobr])",
            "k(x)(a0) -[(SD(m), a0)]-> <a0: SD(h(m, x))>",
            // Reading from states and inputs.
            "k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k(sl)",
            "k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k(sr)",
            "-[ (SD(m), a0) ]-> k(m)"
        };

        await DoTest(ruleSet, "h(init[], left[])", "SD(init[])", true); //, true);
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
        await DoTest(ruleSet, "test(n[])", "SD(init[])", true); // false, true);
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
        await DoTest(ruleSet, "s[]", "SD(init[])", true); // false, true);
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
        await DoTest(ruleSet, "enc(a[], b[])", initState, false); // false, false);
        await DoTest(ruleSet, "enc(b[], a[])", initState, true); // false, true);

        List<string> ruleSet2 = new()
        {
            "-[ ]-> k(test1[])",
            "-[ ]-> k(test2[])",
            "[x ~/> test1[]] k(x)(a0) -[ (SD(init[]), a0) ]-> <a0: SD(x)>",
            "-[ (SD(m), a0) ]-> k(h(m))"
        };
        await DoTest(ruleSet2, "h(test1[])", initState, false); //  false, false);
        await DoTest(ruleSet2, "h(test2[])", initState, true); // true, false);
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
        await DoTest(ruleSet, "something[]", initState, true); // false, true);
    }

    [TestMethod]
    public async Task EnclosedDestructorCheck()
    {
        string initState = "SD(init[])";
        List<string> ruleSet = new()
        {
            "k(enc(x, pk(y))), k(y) -[ ]-> k(dec(enc(x, pk(y)), y))",
            "k(dec(enc(x, pk(y)), y)) -[ ]-> k(x)",
            "k(cell1(x)) -[ ]-> k(cell2(x))",
            "k(cell2(dec(enc(x, pk(y)), y))) -[ ]-> k(cell2(x))",
            "-[ ]-> k(cell1(dec(enc(something[], pk(unkKey[])), key[])))",
            "-[ ]-> k(cell1(dec(enc(something[], pk(key[])), key[])))",
            "-[ ]-> k(key[])"
        };
        await DoTest(ruleSet, "cell2(something[])", initState, true); // false, true);
    }

    [TestMethod]
    public async Task VariableCollisionCheck()
    {
        // Firstly, a quick unification check. This check must pass before we can test further.
        FunctionMessage aMsg = new("LetChannel", new() {
            new FunctionMessage("dec", new() { new VariableMessage("x"), new NameMessage("key") })
        });
        FunctionMessage bMsg = new("LetChannel", new() {
            new FunctionMessage("dec", new() { 
                new FunctionMessage("enc", new() { 
                    new TupleMessage(new() { new NameMessage("value1"), new VariableMessage("x") }), 
                    new FunctionMessage("pk", new() { new VariableMessage("y") })
                }), 
                new VariableMessage("y") 
            })
        });
        Assert.IsTrue(aMsg.IsUnifiableWith(bMsg));
        Assert.IsTrue(bMsg.IsUnifiableWith(aMsg));

        // Now the proper test.
        string initState = "SD(init[])";
        List<string> ruleSet = new()
        {
            "-[ ]-> k(Channel(enc(<value1[], value2[]>, pk(key[]))))",
            "k(Channel(x)) -[ ]-> k(LetCell(dec(x, key[])))",
            "k(LetCell(dec(enc(x, pk(y)), y))) -[ ]-> k(LetCell(x))",
            "k(LetCell(<x, y>)) -[ ]-> k(x)"
        };
        await DoTest(ruleSet, "value1[]", initState, true); // false, true);
    }

    [TestMethod]
    public async Task TupleTransferCheck()
    {
        string initState = "Channel(init[])";
        List<string> ruleSet = new()
        {
            "k(enc(x, pk(y))), k(y) -[ ]-> k(dec(enc(x, pk(y)), y))",
            "k(dec(enc(x, pk(y)), y)) -[ ]-> k(x)",
            "-[ (Channel(init[]), a0) ]-> <a0: Channel(enc(<value1[], value2[]>, pk(key[])))>",
            "-[ (Channel(x), a0) ]-> k(ChannelCell(x))",
            "k(ChannelCell(x)) -[ ]-> k(LetCell(dec(x, key[])))",
            "k(LetCell(dec(enc(x, pk(y)), y))) -[ ]-> k(LetCell(x))",
            "k(LetCell(<x, y>)) -[ ]-> k(x)"
        };
        await DoTest(ruleSet, "value1[]", initState, true); //, false);

        List<string> ruleSet2 = new()
        {
            "-[ ]-> k(right[])",
            "n([bobl])(a0), n([bobr])(a0) -[ (Channel(init[]), a0) ]-> m(enc(<init[], [bobl], [bobr]>, pk(sksd[])))",
            "k(enc(<init[], sl, sr>, pk(sksd[]))), k(right[]) -[ ]-> k(sr)"
        };
        await DoTest(ruleSet2, "[bobr]", initState, true); //, false);
    }

    [TestMethod]
    public async Task TupleDeconstructionCheck()
    {
        string initState = "SD(init[])";
        List<string> ruleSet = new()
        {
            "-[ ]-> k(destr(<enc(s[], k[]), m[]>))",
            "k(destr(<v, w>)) -[ ]-> k(v@cell(v))",
            "k(destr(<v, w>)) -[ ]-> k(w@cell(w))",
            "k(v@cell(v)), k(w@cell(w)) -[ ]-> k(x@cell(dec(v, k[])))",
            "k(x@cell(x)) -[ ]-> k(x)",
            "k(dec(enc(x, y), y)) -[ ]-> k(x)"
        };
        await DoTest(ruleSet, "s[]", initState, true);// false, true);
    }

    private async Task DoTest(
        IEnumerable<string> ruleSrcs,
        string querySrc,
        string stateInitSrc,
        bool shouldFindAttack)
    {
        List<Rule> rules = new(from r in ruleSrcs select Parser.Parse(r));
        IMessage query = MessageParser.ParseMessage(querySrc);
        State initState = MessageParser.ParseState(stateInitSrc);

        QueryEngine qe5 = new(new HashSet<State>() { initState }, query, null, rules);
        bool attackFound = false;
        bool completedDone = false;

        void onAttackAssessedFound(Nession n, IReadOnlySet<HornClause> _, Attack? a) => attackFound |= a != null;
        void onCompletion() => completedDone = true;

        const int testElaborations = 5;
        await qe5.Execute(null, onAttackAssessedFound, onCompletion, testElaborations, false);
        Assert.AreEqual(shouldFindAttack, attackFound);
        Assert.IsTrue(completedDone, "Completion not called");
    }

}

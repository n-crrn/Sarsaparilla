using Microsoft.VisualStudio.TestTools.UnitTesting;
using StatefulHorn;

namespace StatefulHornTest;

[TestClass]
public class ImplicationTests
{
    private readonly RuleFactory Factory = new();

    [TestMethod]
    public void ConstantCheck()
    {
        // know(h[]), know(m[]) -[ ]-> know(n)
        Factory.SetNextLabel("const-first");
        Factory.RegisterPremise(Event.Know(new NameMessage("h")));
        Factory.RegisterPremise(Event.Know(new NameMessage("m")));
        StateConsistentRule r1 = Factory.CreateStateConsistentRule(Event.Know(new NameMessage("n")));

        // know(h[]), know(m[]), know(l[]) -[ ]-> know(n)
        Factory.SetNextLabel("const-second");
        Factory.RegisterPremise(Event.Know(new NameMessage("h")));
        Factory.RegisterPremise(Event.Know(new NameMessage("m")));
        Factory.RegisterPremise(Event.Know(new NameMessage("l")));
        StateConsistentRule r2 = Factory.CreateStateConsistentRule(Event.Know(new NameMessage("n")));

        DoChecks(r1, r2, SigmaMap.Empty);
    }

    [TestMethod]
    public void VariableCheck()
    {
        // know(h[]), know(m) -[ ]-> know(sm(h[], m)
        Factory.SetNextLabel("var-first");
        Factory.RegisterPremise(Event.Know(new NameMessage("h")));
        Factory.RegisterPremise(Event.Know(new VariableMessage("m")));
        IMessage concR1 = new FunctionMessage("sm", new() { new NameMessage("h"), new VariableMessage("m") });
        StateConsistentRule r1 = Factory.CreateStateConsistentRule(Event.Know(concR1));

        // know(h[]), know(k[]) -[ ]-> know(sm(h[], k[])
        Factory.SetNextLabel("var-second");
        Factory.RegisterPremise(Event.Know(new NameMessage("h")));
        Factory.RegisterPremise(Event.Know(new NameMessage("k")));
        IMessage concR2 = new FunctionMessage("sm", new() { new NameMessage("h"), new NameMessage("k") });
        StateConsistentRule r2 = Factory.CreateStateConsistentRule(Event.Know(concR2));

        // Expect SigmaMap with m |-> k[].
        SigmaMap expectedMapping = new(new() { (new VariableMessage("m"), new NameMessage("k")) });

        DoChecks(r1, r2, expectedMapping);
    }

    [TestMethod]
    public void VariableStateCheck()
    {
        // For brevity, create reused events here.
        Event hEvent = Event.Know(new NameMessage("h"));
        Event mEvent = Event.Know(new VariableMessage("m"));
        Event bEvent = Event.Know(new NameMessage("b"));
        Event kEvent = Event.Know(new NameMessage("k"));
        State initState = new("SD", new NameMessage("init"));
        State mState = new("SD", new VariableMessage("m"));
        State kState = new("SD", new NameMessage("k"));
        Event sm_hm = Event.Know(new FunctionMessage("sm", new() { new NameMessage("h"), new VariableMessage("m") }));
        Event sm_hk = Event.Know(new FunctionMessage("sm", new() { new NameMessage("h"), new NameMessage("k") }));

        // know(h[])(1), know(m)(2) : {(1) :: a_1, (2) :: a_1}
        // -[ (SD(init[]), a_0), (SD(m), a_1) : {a_0 <= a_1} ]->
        // know(sm(h[], m)
        Factory.SetNextLabel("var-state-first");
        Snapshot r1Init = Factory.RegisterState(initState);
        Snapshot r1MState = Factory.RegisterState(mState);
        r1MState.SetLaterThan(r1Init);
        Factory.RegisterPremises(r1MState, hEvent, mEvent);
        StateConsistentRule r1 = Factory.CreateStateConsistentRule(sm_hm);

        // know(h[])(1), know(k[])(2), know(b[])(3) : {(1) :: a_1, (2) :: a_1, (3) :: a_1}
        // -[ (SD(init[]), a_0), (SD(k[]), a_1) : {a_0 <= a_1} ]->
        // know(sm(h[], k[])
        Factory.SetNextLabel("var-state-second");
        Snapshot r2Init = Factory.RegisterState(initState);
        Snapshot r2KState = Factory.RegisterState(kState);
        r2KState.SetLaterThan(r2Init);
        Factory.RegisterPremises(r2KState, hEvent, kEvent, bEvent);
        StateConsistentRule r2 = Factory.CreateStateConsistentRule(sm_hk);

        // Expect SigmaMap with m |-> k[].
        SigmaMap expectedMapping = new(new() { (new VariableMessage("m"), new NameMessage("k")) });

        DoChecks(r1, r2, expectedMapping);
    }

    [TestMethod]
    public void SnapshotRelationsCheck()
    {
        // For brevity, create reused messages, states and events here.
        string sd = "SD";
        NameMessage init = new("init");
        NameMessage full = new("full");
        State initState = new(sd, init);
        State fullState = new(sd, full);
        Event knowFull = Event.Know(full);

        // -[ (SD(init[]), a_0), (SD(full[]), a_1) {a_0 ⋖ a_1} ]-> know(full[])
        Factory.SetNextLabel("ss-rel-rule-1");
        Snapshot r1Init = Factory.RegisterState(initState);
        Snapshot r1Full = Factory.RegisterState(fullState);
        r1Full.SetModifiedOnceLaterThan(r1Init);
        StateConsistentRule r1 = Factory.CreateStateConsistentRule(knowFull);

        // The code for this test is retained for future consideration when further processing
        // of snapshots is done in the rule for Algorithm 1 of Li Li et al 2017.
        // -[ (SD(init[]), a_0), (SD(mid[]), a_1), (SD(full[]), a_2) {a_0 <= a_1, a_1 <= a_2} ]-> know(full[])
        /*
        NameMessage mid = new("mid");
        State midState = new(sd, mid);
        Factory.SetNextLabel("ss-rel-rule-2");
        Snapshot r2Init = Factory.RegisterState(initState);
        Snapshot r2Mid = Factory.RegisterState(midState);
        Snapshot r2Full = Factory.RegisterState(fullState);
        r2Full.SetLaterThan(r2Mid);
        r2Mid.SetLaterThan(r2Init);
        StateConsistentRule r2 = Factory.CreateStateConsistentRule(knowFull);*/

        // -[ (SD(init[]), a_0), (SD(full[]), a_1) {a_0 <= a_1} ]-> know(full[])
        Factory.SetNextLabel("ss-rel-rule-3");
        Snapshot r3Init = Factory.RegisterState(initState);
        Snapshot r3Full = Factory.RegisterState(fullState);
        r3Full.SetLaterThan(r3Init);
        StateConsistentRule r3 = Factory.CreateStateConsistentRule(knowFull);

        // Do tests - note that the SigmaMaps are expected to be empty.
        //DoChecks(r1, r2, SigmaMap.Empty);
        DoChecks(r1, r3, SigmaMap.Empty);
    }

    /// <summary>
    /// Conducts assert tests to ensure that r1 can imply r2, r2 cannot imply r1 and that the 
    /// expected mapping is given when r1 implies r2. An additional check is also conducted to
    /// ensure that the SigmaMap returned from attempting to imply r1 from r2 is null.
    /// </summary>
    /// <param name="r1">A rule that can imply r2.</param>
    /// <param name="r2">A rule that can be implied by r1.</param>
    /// <param name="expectedMapping">The mapping required to have r1 imply r2.</param>
    private static void DoChecks(StateConsistentRule r1, StateConsistentRule r2, SigmaMap expectedMapping)
    {
        Assert.IsTrue(r1.CanImply(r2, out SigmaMap? r1r2Map), $"Rule {r1.Label} should imply rule {r2.Label}.");
        Assert.AreEqual(expectedMapping, r1r2Map, "Expect SigmaMap with m |-> k[].");
        Assert.IsFalse(r2.CanImply(r1, out SigmaMap? r2r1Map), $"Rule {r2.Label} should not imply rule {r1.Label}.");
        Assert.IsNull(r2r1Map, "Only a null map should be returned from a failed implication test.");
    }

    /// <summary>
    /// A test of rules combinations that have been found to work in the past when they should
    /// not have.
    /// </summary>
    [TestMethod]
    public void EnsureImplicationFailsCheck()
    {
        RuleParser parser = new();
        Rule r1 = parser.Parse("know(sk), know(aenc(m, pk(sk))) -[ ]-> know(m)");
        Rule r2 = parser.Parse("know(sk), know(aenc(mf, pk(s))) -[ ]-> know(aenc((mf, bob_l[], bob_r[]), pk(sksd[])))");
        Assert.IsFalse(r1.CanImply(r2, out SigmaMap? _), $"Rule {r1} should not imply {r2}.");
    }
}

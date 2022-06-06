using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StatefulHorn;
using StatefulHorn.Messages;

namespace StatefulHornTest;

[TestClass]
public class CompositionTests
{
    private readonly RuleParser Parser = new();

    /// <summary>
    /// Checks that no attempt to combine rules is made when there is no chance of success.
    /// </summary>
    [TestMethod]
    public void ComposeAvoidCheck()
    {
        StateConsistentRule r1 = Parser.ParseStateConsistentRule("k(m[]), k(pub[]) -[ ]-> k(msg1[])");
        StateConsistentRule r2 = Parser.ParseStateConsistentRule("k(msg2[]), k(msg3[]) -[ ]-> k(msg4[])");
        Assert.IsNull(r1.TryComposeUpon(r2));
    }

    /// <summary>
    /// Check that two rules without snapshots can be successfully combined.
    /// </summary>
    [TestMethod]
    public void BasicCompose()
    {
        StateConsistentRule r1 = Parser.ParseStateConsistentRule("k(m[]), k(pub[]) -[ ]-> k(msg1[])");
        StateConsistentRule r2 = Parser.ParseStateConsistentRule("k(msg1[]), k(msg2[]) -[ ]-> k(msg3[])");
        StateConsistentRule expected = Parser.ParseStateConsistentRule("k(m[]), k(pub[]), k(msg2[]) -[ ]-> k(msg3[])");

        StateConsistentRule? derivedRule = r1.TryComposeUpon(r2);
        Assert.IsNotNull(derivedRule);
        Assert.AreEqual(expected, derivedRule);
    }

    /// <summary>
    /// Check that two rules, where only one or the other, has a set of snapshots.
    /// </summary>
    [TestMethod]
    public void PartialSnapshotsCompose()
    {
        string r1Src = "new([bob_l]), new([bob_r]), know(m_f) -[ ]-> know(enc_a(<m_f, [bob_l], [bob_r]>, pk(sksd[])))";
        StateConsistentRule r1 = Parser.ParseStateConsistentRule(r1Src);

        string r2Src = "know(enc_a(<m_f, s_l, s_r>, pk(sksd[])))(1) : {(1) :: a_1} -[ " +
                       "(SD(init[]), a_0), (SD(h(m_f, left[])), a_1) : {a_0 ≤ a_1} ]-> know(s_l)";
        StateConsistentRule r2 = Parser.ParseStateConsistentRule(r2Src);

        string expectedSrc = "new([bob_l])(1), new([bob_r])(2), know(m_f)(3) : " +
            "{(1) :: a_1, (2) :: a_1, (3) :: a_1} -[ " +
            "(SD(init[]), a_0), (SD(h(m_f, left[])), a_1) : {a_0 ≤ a_1} ]-> " +
            "know([bob_l])";
        StateConsistentRule expected = Parser.ParseStateConsistentRule(expectedSrc);

        StateConsistentRule? derived = r1.TryComposeUpon(r2);
        Assert.IsNotNull(derived, "Failed to assess composition correctly.");
        Assert.AreEqual(expected, derived, "Derived rule not as expected.");
    }

    /// <summary>
    /// Check that two rules (both with snapshots) can be composed.
    /// </summary>
    [TestMethod]
    public void BothSnapshotsCompose()
    {
        StateConsistentRule r4 = (StateConsistentRule)Example1.GetRule(4);
        StateTransferringRule r6 = (StateTransferringRule)Example1.GetRule(6);
        RuleParser parser = new();
        Rule expected = parser.Parse("k(enc_a(<m_f, x, s_r>, pk(sksd[])))(1) : {(1) :: a1, (1) :: a3} -[ " +
            "(SD(init[]), a0), (SD(h(m_f, left[])), a1), (SD(init[]), a2), (SD(m), a3) : " + 
            "{ a0 =< a1, a2 =< a3 } ]-> <a1: SD(h(m, x))>");
        
        Assert.IsTrue(r4.TryComposeWith(r6, out Rule? derivedRule), "Failed to assess composition correctly.");
        Assert.IsNotNull(derivedRule);
        Assert.AreEqual(expected, derivedRule);
    }

    /// <summary>
    /// It is common for there to be rules with similarly named variables. However, these
    /// variables need to be treated separately for the purpose of composition. This tests
    /// that it is the case.
    /// </summary>
    [TestMethod]
    public void SameVariablesNameCompose()
    {
        // Create the factory object and common messages.
        RuleFactory factory = new();
        IMessage xMsg = new VariableMessage("x");
        IMessage yMsg = new VariableMessage("y");
        IMessage encXYMsg = new FunctionMessage("enc", new() { xMsg, yMsg });
        IMessage zMsg = new NameMessage("z");
        IMessage encZYMsg = new FunctionMessage("enc", new() { zMsg, yMsg });
        IMessage encXYZMsg = new FunctionMessage("enc", new() { zMsg, encXYMsg });

        // Rules to compose.
        factory.SetNextLabel("R1");
        factory.RegisterPremises(Event.Know(xMsg), Event.Know(yMsg));
        StateConsistentRule r1 = factory.CreateStateConsistentRule(Event.Know(encXYMsg));
        factory.SetNextLabel("R2");
        factory.RegisterPremise(Event.Know(yMsg));
        StateConsistentRule r2 = factory.CreateStateConsistentRule(Event.Know(encZYMsg));

        // Expected result.
        factory.SetNextLabel("Expected");
        factory.RegisterPremises(Event.Know(xMsg), Event.Know(yMsg));
        StateConsistentRule expectedResult = factory.CreateStateConsistentRule(Event.Know(encXYZMsg));

        // Do the actual test.
        Assert.IsTrue(r1.TryComposeWith(r2, out Rule? result), "Unable to compose rules.");
        Assert.AreEqual(expectedResult, result, "Composition was not as expected.");
    }

    /// <summary>
    /// There is a situation where a composition may result in a rule where the result
    /// is a premise. This check ensures that this situation is caught and rectified.
    /// </summary>
    [TestMethod]
    public void InvalidCompose()
    {
        RuleParser parser = new();

        StateConsistentRule r1 = parser.ParseStateConsistentRule("k(m), k(pub) -[ ]-> k(aenc(m, pub))");
        StateConsistentRule r2 = parser.ParseStateConsistentRule("k(sk), k(aenc(m, pk(sk))) -[ ]-> k(m)");

        string msg = "Should not be able to compose rules where result is in premise.";
        Assert.IsFalse(r1.TryComposeWith(r2, out Rule? result) , msg);
        Assert.IsNull(result, "Result should be null from failed attempt at composing.");
    }

    private static void ShouldNotBeUnifiable(Event ev1, Event ev2)
    {
        Debug.WriteLine($"Attempted to unify {ev1} and {ev2}");
        SigmaFactory sf = new();
        bool canBeUnified = ev1.CanBeUnifiableWith(ev2, new(), new(), sf);
        if (canBeUnified)
        {
            Debug.WriteLine("Sigma Maps are as follows:");
            Debug.WriteLine(sf.CreateForwardMap().ToString());
            Debug.WriteLine(sf.CreateBackwardMap().ToString());
        }
        Assert.IsFalse(canBeUnified);
    }

}

﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using StatefulHorn;

namespace StatefulHornTest;

[TestClass]
public class CompositionTests
{
    private static StateConsistentRule CreateBasicRule(RuleFactory factory)
    {
        factory.SetNextLabel("basic1");
        factory.RegisterPremise(Event.Know(new NameMessage("m")));
        factory.RegisterPremise(Event.Know(new NameMessage("pub")));
        return factory.CreateStateConsistentRule(Event.Know(new NameMessage("msg1")));
    }

    /// <summary>
    /// Checks that no attempt to combine rules is made when there is no chance of success.
    /// </summary>
    [TestMethod]
    public void ComposeAvoidCheck()
    {
        RuleFactory factory = new();
        StateConsistentRule r1 = CreateBasicRule(factory);

        factory.SetNextLabel("basic2");
        factory.RegisterPremise(Event.Know(new NameMessage("msg2")));
        factory.RegisterPremise(Event.Know(new NameMessage("msg3")));
        StateConsistentRule r2 = factory.CreateStateConsistentRule(Event.Know(new NameMessage("msg4")));

        Assert.IsFalse(r1.TryComposeWith(r2, out Rule? derivedRule));
        Assert.IsNull(derivedRule);
    }

    /// <summary>
    /// Check that two rules without snapshots can be successfully combined.
    /// </summary>
    [TestMethod]
    public void BasicCompose()
    {
        RuleFactory factory = new();
        StateConsistentRule r1 = CreateBasicRule(factory);

        factory.SetNextLabel("basic2");
        factory.RegisterPremise(Event.Know(new NameMessage("msg1")));
        factory.RegisterPremise(Event.Know(new NameMessage("msg2")));
        StateConsistentRule r2 = factory.CreateStateConsistentRule(Event.Know(new NameMessage("msg3")));

        Assert.IsTrue(r1.TryComposeWith(r2, out Rule? derivedRule));
        Assert.IsNotNull(derivedRule);
        Assert.AreEqual("know(m[]), know(pub[]), know(msg2[]) -[ ]-> know(msg3[])", derivedRule.Describe());
    }

    /// <summary>
    /// Check that two rules, where only one or the other, has a set of snapshots.
    /// </summary>
    [TestMethod]
    public void PartialSnapshotsCompose()
    {
        // For this test, use the Example1 rules for composition.
        Rule r3Basic = Example1.GetRule(3);
        StateConsistentRule r3 = (StateConsistentRule)r3Basic;
        Assert.AreEqual("new([bob_l], l_sl[]), new([bob_r], l_sr[]), know(m_f) -[ ]-> " + 
                        "know(enc_a(<m_f, [bob_l], [bob_r]>, pk(sksd[])))",
                        r3.Describe());

        Rule r4 = Example1.GetRule(4);
        Assert.AreEqual("know(enc_a(<m_f, s_l, s_r>, pk(sksd[])))(1) : {(1) :: a_1} -[ " + 
                        "(SD(init[]), a_0), (SD(h(m_f, left[])), a_1) : {a_0 ≤ a_1} ]-> know(s_l)",
                        r4.Describe());

        Assert.IsTrue(r3.TryComposeWith(r4, out Rule? derivedRule), "Failed to assess composition correctly.");
        Assert.IsNotNull(derivedRule);
        string expected = "new([bob_l], l_sl[])(1), new([bob_r], l_sr[])(2), know(m_f)(3) : " +
            "{(1) :: a_1, (2) :: a_1, (3) :: a_1} -[ " +
            "(SD(init[]), a_0), (SD(h(m_f, left[])), a_1) : {a_0 ≤ a_1} ]-> " +
            "know([bob_l])";
        Assert.AreEqual(expected, derivedRule.Describe());
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
}
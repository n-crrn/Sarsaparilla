using Microsoft.VisualStudio.TestTools.UnitTesting;
using StatefulHorn;

namespace StatefulHornTest;

/// <summary>
/// Instances of this class manage tests of the construction of StatefulHorn rules. These 
/// tests are end-to-end, so the rules are constructed and their textual descriptions are
/// checked.
/// </summary>
[TestClass]
public class CreationTests
{
    /// <summary>
    /// The rule factory used by all methods of this object. We do not re-initialise the 
    /// factory between tests as we want to ensure that it will reset correctly.
    /// </summary>
    private readonly RuleFactory Factory;

    public CreationTests()
    {
        Factory = new();
    }

    /// <summary>
    /// Tests that basic rules without states can be created successfully.
    /// </summary>
    [TestMethod]
    public void StateConsistentRuleCreation()
    {
        Factory.SetNextLabel("encryption");
        Factory.RegisterPremise(Event.Know(new VariableMessage("m")));
        Factory.RegisterPremise(Event.Know(new VariableMessage("pub")));
        Rule r = Factory.CreateStateConsistentRule(Event.Know(new FunctionMessage("enc_a", new() { new VariableMessage("m"), new VariableMessage("pub") })));

        Assert.AreEqual("encryption = know(m), know(pub) -[ ]-> know(enc_a(m, pub))", r.ToString());
    }

    /// <summary>
    /// Tests that state consistent rules can be created successfully.
    /// </summary>
    [TestMethod]
    public void StateConsistentStatefulRuleCreation()
    {
        Factory.SetNextLabel("sdReplyLeft");
        Snapshot r4Init = Factory.RegisterState(new State("SD", new NameMessage("init")));

        State sdMfLeftState = new("SD", new FunctionMessage("h", new() { new NameMessage("m_f"), new NameMessage("left") }));
        Snapshot r4SdMfLeftState = Factory.RegisterState(sdMfLeftState);

        Event fullEncAKnows = Event.Know(
            new FunctionMessage("enc_a",
                new()
                {
                    new TupleMessage(new() { new NameMessage("m_f"), new NonceMessage("bob_l"), new NonceMessage("bob_r") }),
                    new FunctionMessage("pk", new() { new NameMessage("sksd") })
                }));
        Factory.RegisterPremises(r4SdMfLeftState, fullEncAKnows);
        r4SdMfLeftState.SetLaterThan(r4Init);
        Rule r = Factory.CreateStateConsistentRule(Event.Know(new VariableMessage("s_l")));

        string expected = "sdReplyLeft = know(enc_a(<m_f[], [bob_l], [bob_r]>, pk(sksd[])))(1) : {(1) :: a_1} " +
            "-[ (SD(init[]), a_0), (SD(h(m_f[], left[])), a_1) : {a_0 ≤ a_1} ]-> know(s_l)";
        Assert.AreEqual(expected, r.ToString());
    }

    /// <summary>
    /// Tests that state transferring rules can be created.
    /// </summary>
    [TestMethod]
    public void StateTransferringRuleCreation()
    {
        IMessage mMsg = new VariableMessage("m");
        IMessage xMsg = new VariableMessage("x");

        State sdInitState = new("SD", new NameMessage("init"));
        State sdMState = new("SD", mMsg);
        State sdHVarState = new("SD", new FunctionMessage("h", new() { mMsg, xMsg }));

        Factory.SetNextLabel("stateChange");
        Snapshot r6Init = Factory.RegisterState(sdInitState);
        Snapshot r6M = Factory.RegisterState(sdMState);
        r6M.SetLaterThan(r6Init);
        Factory.RegisterPremises(r6M, Event.Know(xMsg));
        r6M.TransfersTo = sdHVarState;
        Rule r = Factory.CreateStateTransferringRule();

        string expected = "stateChange = know(x)(1) : {(1) :: a_1} -[ " +
            "(SD(init[]), a_0), (SD(m), a_1) : {a_0 ≤ a_1} ]-> " +
            "<a_1: SD(h(m, x))>";
        Assert.AreEqual(expected, r.ToString());
    }
}

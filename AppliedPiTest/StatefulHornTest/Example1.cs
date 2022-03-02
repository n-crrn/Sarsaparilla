using System.Collections.Generic;
using StatefulHorn;
using StatefulHorn.Messages;

namespace StatefulHornTest;

/// <summary>
/// Namespace that provides a set of 12 rules pre-built rules that can be used for testing
/// algorithms of the Stateful Horn library. These examples come from the paper Li Li et al 2017.
/// </summary>
public static class Example1
{
    private static readonly List<Rule> BasisRules;

    static Example1()
    {
        BasisRules = new();

        RuleFactory factory = new();

        // --- Commonly used messages ---
        IMessage mMsg = new VariableMessage("m");
        IMessage pubMsg = new VariableMessage("pub");
        IMessage encAMsg = new FunctionMessage("enc_a", new() { mMsg, pubMsg });
        IMessage skMsg = new VariableMessage("sk");
        IMessage pkSkMsg = new FunctionMessage("pk", new() { skMsg });
        IMessage fullEncAMsg = new FunctionMessage("enc_a", new() { mMsg, pkSkMsg });
        NonceMessage bobLMsg = new("bob_l");
        NonceMessage bobRMsg = new("bob_r");
        IMessage mFMsg = new VariableMessage("m_f");
        IMessage sksdMsg = new NameMessage("sksd");
        IMessage pkSksdMsg = new FunctionMessage("pk", new() { sksdMsg });
        IMessage leftMsg = new NameMessage("left");
        IMessage rightMsg = new NameMessage("right");
        IMessage slMsg = new VariableMessage("s_l");
        IMessage srMsg = new VariableMessage("s_r");
        IMessage xMsg = new VariableMessage("x");

        // --- Commonly used states ---
        State sdInitState = new("SD", new NameMessage("init"));
        State sdMState = new("SD", new VariableMessage("m"));
        State sdMfLeftState = new("SD", new FunctionMessage("h", new() { mFMsg, leftMsg }));
        State sdMfRightState = new("SD", new FunctionMessage("h", new() { mFMsg, rightMsg }));
        State sdHVarState = new("SD", new FunctionMessage("h", new() { mMsg, xMsg }));

        // --- Rule 1 - Encryption ---
        factory.SetNextLabel("encryption");
        factory.RegisterPremise(Event.Know(mMsg));
        factory.RegisterPremise(Event.Know(pubMsg));
        BasisRules.Add(factory.CreateStateConsistentRule(Event.Know(encAMsg)));

        // --- Rule 2 - Decryption ---
        factory.SetNextLabel("decryption");
        factory.RegisterPremise(Event.Know(fullEncAMsg));
        factory.RegisterPremise(Event.Know(skMsg));
        BasisRules.Add(factory.CreateStateConsistentRule(Event.Know(mMsg)));

        // --- Rule 3 - Bob ---
        factory.SetNextLabel("bob");
        factory.RegisterPremise(Event.New(bobLMsg));
        factory.RegisterPremise(Event.New(bobRMsg));
        factory.RegisterPremise(Event.Know(mFMsg));
        TupleMessage encAInnerSeq = new(new() { mFMsg, bobLMsg, bobRMsg });
        Event fullEncAKnows = Event.Know(new FunctionMessage("enc_a", new() { encAInnerSeq, pkSksdMsg }));
        BasisRules.Add(factory.CreateStateConsistentRule(fullEncAKnows));

        // --- Rule 4 - SD replying left ---
        factory.SetNextLabel("sdReplyLeft");
        Snapshot r4Init = factory.RegisterState(sdInitState);
        Snapshot r4SdMfLeftState = factory.RegisterState(sdMfLeftState);
        TupleMessage varEncA = new(new() { new VariableMessage("m_f"), new VariableMessage("s_l"), new VariableMessage("s_r") });
        factory.RegisterPremises(r4SdMfLeftState, Event.Know(new FunctionMessage("enc_a", new() { varEncA, pkSksdMsg })));
        r4SdMfLeftState.SetLaterThan(r4Init);
        BasisRules.Add(factory.CreateStateConsistentRule(Event.Know(slMsg)));

        // --- Rule 5 - How to obtain information from states ---
        factory.SetNextLabel("read");
        Snapshot initSS = factory.RegisterState(sdInitState);
        Snapshot mSS = factory.RegisterState(sdMState);
        mSS.SetLaterThan(initSS);
        BasisRules.Add(factory.CreateStateConsistentRule(Event.Know(mMsg)));

        // --- Rule 6 - State changing for SD ---
        factory.SetNextLabel("stateChange");
        Snapshot r6Init = factory.RegisterState(sdInitState);
        Snapshot r6M = factory.RegisterState(sdMState);
        r6M.SetLaterThan(r6Init);
        factory.RegisterPremises(r6M, Event.Know(xMsg));
        r6M.TransfersTo = sdHVarState;
        BasisRules.Add(factory.CreateStateTransferringRule());

        // --- Rule 7 - Public key ---
        factory.SetNextLabel("publicKey");
        factory.RegisterPremise(Event.Know(skMsg));
        BasisRules.Add(factory.CreateStateConsistentRule(Event.Know(pkSkMsg)));

        // --- Rule 8 - SD replying right ---
        factory.SetNextLabel("sdReplyRight");
        Snapshot r8Init = factory.RegisterState(sdInitState);
        Snapshot r8MfRightState = factory.RegisterState(sdMfRightState);
        factory.RegisterPremises(r8MfRightState, fullEncAKnows);
        r8MfRightState.SetLaterThan(r8Init);
        BasisRules.Add(factory.CreateStateConsistentRule(Event.Know(srMsg)));

        // --- Rule 9 - Property of Interest ---
        factory.SetNextLabel("interest");
        factory.RegisterPremise(Event.New(bobLMsg));
        factory.RegisterPremise(Event.New(bobRMsg));
        factory.RegisterPremise(Event.Know(bobLMsg));
        factory.RegisterPremise(Event.Know(bobRMsg));
        BasisRules.Add(factory.CreateStateConsistentRule(Event.Leak(new TupleMessage(new() { bobLMsg, bobRMsg }))));
    }

    /// <summary>
    /// Get the rule corresponding with the number in the paper Li Li et al 2017.
    /// </summary>
    /// <param name="id">Number identifying the rule, between 1 and 12 inclusive.</param>
    /// <returns>A Rule object representing the requested rule.</returns>
    public static Rule GetRule(int id)
    {
        return BasisRules[id - 1];
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using StatefulHorn;

namespace StatefulHornTest;

/// <summary>
/// 
/// </summary>
[TestClass]
public class ParseTests
{
    private readonly RuleFactory Factory = new();

    #region Correct parsing tests.

    /// <summary>
    /// Tests the creation of a set of basic state consistent rules, which are equivalent
    /// symbolically but with different spacings.
    /// </summary>
    [TestMethod]
    public void StateConsistentCheck()
    {
        RuleParser parser = new();
        string spacedRule = "know(m), know(pub) -[ ]-> know(enc(m, pub))";
        string nonspacedRule = spacedRule.Replace(" ", "");
        string extraSpacedRule = spacedRule.Replace(" ", "   ").Replace("(", " ( ").Replace(")", " ) ");

        Factory.SetNextLabel("consist-test");
        Factory.RegisterPremise(Event.Know(new VariableMessage("m")));
        Factory.RegisterPremise(Event.Know(new VariableMessage("pub")));
        Event k_enc = Event.Know(new FunctionMessage("enc", new() { new VariableMessage("m"), new VariableMessage("pub")}));
        Rule expectedRule = Factory.CreateStateConsistentRule(k_enc);

        AssertRulesEqual(expectedRule, parser.Parse(spacedRule), spacedRule);
        AssertRulesEqual(expectedRule, parser.Parse(nonspacedRule), nonspacedRule);
        AssertRulesEqual(expectedRule, parser.Parse(extraSpacedRule), extraSpacedRule);
    }

    /// <summary>
    /// Tests the creation of a set of basic state transfer rules, which are equivalent
    /// symbolically but with different spacings.
    /// </summary>
    [TestMethod]
    public void StateTransferringCheck()
    {
        RuleParser parser = new();
        string spacedRule = "know(x)(1) : {(1) :: a3} -[ (SD(init[]), a0), (SD(m), a3) : {a0 =< a3} ]-> <a3: SD(h(m, x))>";
        string nonspacedRule = spacedRule.Replace(" ", "");
        string extraSpacedRule = spacedRule.Replace(" ", "   ").Replace("(", " ( ").Replace(")", " ) ");
        string shorthandRule = "know(x)(a3) -[ (SD(init[]), a0), (SD(m), a3) : {a0 =< a3} ]-> <a3: SD(h(m, x))>";

        Factory.SetNextLabel("transfer-test");
        Snapshot a0 = Factory.RegisterState(new State("SD", new NameMessage("init")));
        Snapshot a3 = Factory.RegisterState(new State("SD", new VariableMessage("m")));
        a3.SetLaterThan(a0);
        Factory.RegisterPremises(a3, Event.Know(new VariableMessage("x")));
        a3.TransfersTo = new State("SD", new FunctionMessage("h", new() { new VariableMessage("m"), new VariableMessage("x") }));
        Rule expectedRule = Factory.CreateStateTransferringRule();

        AssertRulesEqual(expectedRule, parser.Parse(spacedRule), spacedRule);
        AssertRulesEqual(expectedRule, parser.Parse(nonspacedRule), nonspacedRule);
        AssertRulesEqual(expectedRule, parser.Parse(extraSpacedRule), extraSpacedRule);
        AssertRulesEqual(expectedRule, parser.Parse(shorthandRule), shorthandRule);
    }

    // FIXME: Include additional checks for snapshot ordering constraint specification.

    /// <summary>
    /// Check that all event name shortcuts (e.g. "k" for "know") all work.
    /// </summary>
    [TestMethod]
    public void EventShortcutsCheck()
    {
        RuleParser parser = new();
        string ruleSrcStart = "k(x), i(m), n([nonce], k[]) -[ ]-> ";// k(t(x,m)), l(m), a(x)";
        string[] endingsSrc = new string[]
        {
            "k(t(x, m))",
            "l(m)",
            "a(m)"
        };
        Event[] endings = new Event[]
        {
            Event.Know(new FunctionMessage("t", new() {new VariableMessage("x"), new VariableMessage("m")})),
            Event.Leak(new VariableMessage("m")),
            Event.Accept(new VariableMessage("m"))
        };
        Event[] premises = new Event[]
        {
            Event.Know(new VariableMessage("x")),
            Event.Init(new VariableMessage("m")),
            Event.New(new NonceMessage("nonce"), new NameMessage("k"))
        };

        for (int i = 0; i < endings.Length; i++)
        {
            Factory.SetNextLabel($"shortcut-{i}");
            Factory.RegisterPremises(premises);
            Rule expectedRule = Factory.CreateStateConsistentRule(endings[i]);
            string ruleSrc = ruleSrcStart + endingsSrc[i];
            AssertRulesEqual(expectedRule, parser.Parse(ruleSrc), ruleSrc);
        }
    }

    /// <summary>
    /// Ensure labels are correctly parsed.
    /// </summary>
    [TestMethod]
    public void LabelParsingCheck()
    {
        RuleParser parser = new();
        string expectedLabel = "this test rule";

        // --- General check ---
        string generalLabelInput = $"{expectedLabel} = know(x), know(y) -[ ]-> know(enc(x, y))";
        Rule generalParsed = parser.Parse(generalLabelInput);
        Rule generalExpected = CreateDefaultRuleWithGuard(expectedLabel, new(), new());

        // Note that the labels are not tested as part of rule equality, but we need to ensure
        // that the substance of the rules is correctly parsed.
        AssertRulesEqual(generalExpected, generalParsed, generalLabelInput);
        Assert.AreEqual(expectedLabel, generalParsed.Label, "Labels do not match for general case.");

        // --- Empty guard and premise check ---
        string emptyGHMInput = $"{expectedLabel} = -[ ]-> know(z[])";
        Rule emptyGHMParsed = parser.Parse(emptyGHMInput);
        Factory.SetNextLabel("expected GHM");
        Rule emptyGHMExpected = Factory.CreateStateConsistentRule(Event.Know(new NameMessage("z")));
        AssertRulesEqual(emptyGHMExpected, emptyGHMParsed, emptyGHMInput);
        Assert.AreEqual(expectedLabel, emptyGHMParsed.Label, "Labels do not match when guard and premise empty.");

        // --- With guard but empty premise check ---
        string emptyHMInput = $"{expectedLabel} = [name[] ~/> x] -[ ]-> know(z[])";
        Rule emptyHMParsed = parser.Parse(emptyHMInput);
        Factory.SetNextLabel("expected HM");
        Factory.GuardStatements = Guard.CreateFromSets(new() { (new NameMessage("name"), new VariableMessage("x")) }, new());
        Rule emptyHMExpected = Factory.CreateStateConsistentRule(Event.Know(new NameMessage("z")));
        AssertRulesEqual(emptyHMExpected, emptyHMParsed, emptyHMInput);
        Assert.AreEqual(expectedLabel, emptyHMParsed.Label, "Labels do not match when premise empty.");
    }

    /// <summary>
    /// Ensure that guard statements can be correctly parsed.
    /// </summary>
    [TestMethod]
    public void GuardLogicCheck()
    {
        RuleParser parser = new();

        string[][] parsedRules = new string[][]
        {
            ContractExpandRuleString("[name[] ~/> x] know(x), know(y) -[ ]-> know(enc(x, y))"),
            ContractExpandRuleString("[name[] =/= x] know(x), know(y) -[ ]-> know(enc(x, y))"),
            ContractExpandRuleString("[name[] ~/> x, name =/= y] know(x), know(y) -[ ]-> know(enc(x, y))")
        };

        IMessage nameMsg = new NameMessage("name");
        IMessage xMsg = new VariableMessage("x");
        IMessage yMsg = new VariableMessage("y");

        List<Rule> expectedRules = new()
        {
            CreateDefaultRuleWithGuard("r1", new() { (nameMsg, xMsg) }, new()),
            CreateDefaultRuleWithGuard("r2", new(), new() { (nameMsg, xMsg) }),
            CreateDefaultRuleWithGuard("r3", new() { (nameMsg, xMsg) }, new() { (nameMsg, yMsg) })
        };

        for (int i = 0; i < expectedRules.Count; i++)
        {
            Rule expected = expectedRules[i];
            foreach (string src in parsedRules[i])
            {
                Rule generated = parser.Parse(src);
                AssertRulesEqual(expected, generated, src);
            }
        }
    }

    private static string[] ContractExpandRuleString(string inputRule)
    {
        return new string[]
        {
            inputRule,
            inputRule.Replace(" ", ""),
            inputRule.Replace(" ", "  ").Replace("(", " ( ").Replace(")", " ) ").Replace("-[ ]->", "-[   ]->")
        };
    }

    /// <summary>
    /// Uses the Factory member to create a "standard rule", which is 
    /// "know(x), know(y) -[ ]-> know(enc(x, y))". This rule is used when other aspects of the
    /// rule parsing need to be tested (e.g. guard parse testing, label testing).
    /// </summary>
    /// <param name="label">Label to set for the ruel.</param>
    private Rule CreateDefaultRuleWithGuard(string label, List<(IMessage, IMessage)> ununified, List<(IMessage, IMessage)> ununifiable)
    {
        Factory.GuardStatements = Guard.CreateFromSets(new(ununified), new(ununifiable));
        Factory.SetNextLabel(label);
        IMessage xMsg = new VariableMessage("x");
        IMessage yMsg = new VariableMessage("y");
        Factory.RegisterPremises(Event.Know(xMsg), Event.Know(yMsg));
        IMessage encMsg = new FunctionMessage("enc", new List<IMessage> { xMsg, yMsg });
        return Factory.CreateStateConsistentRule(Event.Know(encMsg));
    }

    #endregion
    #region Error catching logic tests.

    /// <summary>
    /// Tests that common instances of invalidly formatted rules are caught and handled as
    /// RuleParserExceptions. I am particularly interested in cases where there are length
    /// bound failures or the parser returns a seemingly valid rule.
    /// </summary>
    [TestMethod]
    public void FormattingErrorCheck()
    {
        string[] testStrings = new string[]
        {
            "know(x) -[ (SD(init[], a0), (SD(m), a3) : {a0 =< a3} ]-> <a3: SD(h(m, x))>",    // Premise needs snapshot reference.
            "know(x)(a2) -[ SD(init[], a0), (SD(m), a3) : {a0 =< a3) ]-> <a3: SD(h(m, x))>", // Premise snapshot reference invalid.
            "know(x) -[ ]-> <a3: SD(h(m, x))>",                        // Attempt to create state transition without snapshot tree.
            "know(x) <a3: SD(h(m, x))>",                               // Completely incorrect form.
            "know(x), know(m) ]-> -[ know(enc(m, pub))",               // State operators around the wrong way.
            "know(x -[ ]-> know(enc(m, pub))",                         // Unclosed term in premise.
            "know(x), know(m) -[ ]-> know(enc(m, pub)",                // Unclosed term in result.
            "know(x)(a3) -[ (SD(init[]), a0), (SD(m), a3) : {a0 =< a3} ]-> <a3: SD(h(m, x))>, know(enc(m, x))", // Cannot be both types.
            "",         // Empty string.
            "-[]->"     // Empty premises, snapshots and results.
        };
        RuleParser parser = new();
        foreach (string ts in testStrings)
        {
            string errMsg = $"Fails to throw RuleParseException when parsing '{ts}'.";
            Assert.ThrowsException<RuleParseException>(() => parser.Parse(ts), errMsg);
        }
    }

    /// <summary>
    /// Check that invalid premises are caught during rule construction.
    /// </summary>
    [TestMethod]
    public void PremiseParsingLogic()
    {
        string[] testStrings = new string[]
        {
            "accept(m1, m2) -[ ]-> know(m1)",
            "leak(m1) -[ ]-> know(m2)",
            "know(x) -[ ]-> know(x)"
        };
        RuleParser parser = new();
        foreach (string ts in testStrings)
        {
            string errMsg = $"Fails to throw RuleConstructionException when parsing '{ts}'.";
            Assert.ThrowsException<RuleConstructionException>(() => parser.Parse(ts), errMsg, errMsg);
        }
    }

    /// <summary>
    /// Ensure that ordering inconsistencies are caught during rule parsing and construction.
    /// </summary>
    [TestMethod]
    public void SnapshotParsingLogic()
    {
        string[] testStrings = new string[]
        {
            "-[ (SD(init[], a0), (SD(m), a1) : { a0 <@ a1, a1 <@ a0 } ]-> know(m)",
            "-[ (SD(init[], a0), (SD(m[]), a1), (SD(n[]), a2) : { a0 =< a1, a1 =< a2, a2 <= a0 } ]-> know(n[])"
        };
        RuleParser parser = new();
        foreach (string ts in testStrings)
        {
            string errMsg = $"Fails to throw RuleParsingException when parsing '{ts}'.";
            Assert.ThrowsException<RuleParseException>(() => parser.Parse(ts), errMsg, errMsg);
        }
    }

    /// <summary>
    /// Ensure that invalid results are caught during rule construction.
    /// </summary>
    [TestMethod]
    public void ResultParsingLogic()
    {
        string[] testStrings = new string[]
        {
            "know(m) -[ ]-> init(m)",
            "know(m) -[ ]-> new([n], l[])"
        };
        RuleParser parser = new();
        foreach (string ts in testStrings)
        {
            string errMsg = $"Fails to throw RuleConstructionException when parsing '{ts}'.";
            Assert.ThrowsException<RuleConstructionException>(() => parser.Parse(ts), errMsg, errMsg);
        }
    }

    #endregion
    #region Redundancy handling.

    /// <summary>
    /// Ensure that the correct logic is followed for handling premises that may be redundant.
    /// </summary>
    [TestMethod]
    public void RedundantLogic()
    {
        RuleParser parser = new();

        string premiseCheckSrc = "know(x), know(y), know(x) -[ ]-> know(enc(x, y))";
        Rule expectedPremiseRule = CreateDefaultRuleWithGuard("premise", new(), new());
        AssertRulesEqual(expectedPremiseRule, parser.Parse(premiseCheckSrc), premiseCheckSrc);

        // The following messages and states are used multiple times below.
        IMessage xMsg = new VariableMessage("x");
        IMessage yMsg = new VariableMessage("y");
        IMessage encMsg = new FunctionMessage("enc", new() { xMsg, yMsg });
        IMessage initMsg = new NameMessage("init");
        IMessage nextMsg = new NameMessage("next");
        IMessage otherMsg = new NameMessage("other");
        State sdInit = new("SD", initMsg);
        State sdNext = new("SD", nextMsg);
        State sdOther = new("SD", otherMsg);

        // Check if identical premises associated with different snapshots are handled correctly.
        string ssCheckSrc = "k(x)(a1), k(y)(a1), k(x)(a3), k(y)(a3) -[ " + 
            "(SD(init[]), a0), (SD(next[]), a1), (SD(init[]), a2), (SD(other[]), a3) : {a0 =< a1, a2 =< a3} ]-> k(enc(x,y))";
        Factory.SetNextLabel("with-ss");
        Snapshot initSS1 = Factory.RegisterState(sdInit); // First trace start.
        Snapshot nextSS = Factory.RegisterState(sdNext);
        Snapshot initSS2 = Factory.RegisterState(sdInit); // Second trace start.
        Snapshot otherSS = Factory.RegisterState(sdOther);
        nextSS.SetLaterThan(initSS1);
        otherSS.SetLaterThan(initSS2);
        Factory.RegisterPremises(nextSS, Event.Know(xMsg), Event.Know(yMsg));
        Factory.RegisterPremises(otherSS, Event.Know(xMsg), Event.Know(yMsg));
        Rule expectedSSRule = Factory.CreateStateConsistentRule(Event.Know(encMsg));
        Rule createdSSRule = parser.Parse(ssCheckSrc);
        Debug.WriteLine(createdSSRule);
        AssertRulesEqual(expectedSSRule, createdSSRule, ssCheckSrc);

        // Check that perfectly identical traces are removed correctly from the SnapshotTree
        // correctly.
        string traceCheckSrc = "k(x)(a1), k(y)(a1), k(x)(a3), k(y)(a3) -[ " + 
            "(SD(init[]), a0), (SD(next[]), a1), (SD(init[]), a2), (SD(next[]), a3) : {a0 =< a1, a2 =< a3} ]-> k(enc(x,y))";
        Factory.SetNextLabel("expected-trace");
        Snapshot tInitSS1 = Factory.RegisterState(sdInit); // First trace start.
        Snapshot tNextSS1 = Factory.RegisterState(sdNext);
        Snapshot tInitSS2 = Factory.RegisterState(sdInit); // Second trace start.
        Snapshot tNextSS2 = Factory.RegisterState(sdNext);
        tNextSS1.SetLaterThan(tInitSS1);
        tNextSS2.SetLaterThan(tInitSS2);
        Factory.RegisterPremises(tNextSS1, Event.Know(xMsg), Event.Know(yMsg));
        Factory.RegisterPremises(tNextSS2, Event.Know(xMsg), Event.Know(yMsg));
        Rule expectedTraceRule = Factory.CreateStateConsistentRule(Event.Know(encMsg));
        Rule createdTraceRule = parser.Parse(traceCheckSrc);
        Debug.WriteLine(createdTraceRule);
        AssertRulesEqual(expectedTraceRule, createdTraceRule, traceCheckSrc);
    }

    #endregion
    #region Convenience methods.

    /// <summary>
    /// Provides a convenient method by which to check that a parsed rule matches the expected
    /// outcome. If the rule does not match, additional debugging data is output to speed up
    /// determining where the mis-match is.
    /// where the 
    /// </summary>
    /// <param name="expected">The reference rule, created using RuleFactory method calls.</param>
    /// <param name="created">The rule created using RuleParser.</param>
    /// <param name="ruleDescription">The original source code passed to RuleParser.</param>
    private static void AssertRulesEqual(Rule expected, Rule created, string ruleDescription)
    {
        try
        {
            Assert.AreEqual(expected, created, $"Rule not parsed correctly: '{ruleDescription}'");
        }
        catch (Exception)
        {
            DebugEquivalence(expected, created);
            throw;
        }
    }

    /// <summary>
    /// Compares the guards, premises, snapshot tree and results of two rules, and outputs where
    /// they match to aid debugging. The output is written using Debug.WriteLine.
    /// </summary>
    /// <param name="expected">The reference rule.</param>
    /// <param name="underTest">The rule under test.</param>
    private static void DebugEquivalence(Rule expected, Rule underTest)
    {
        bool guardsMatch = expected.GuardStatements.Equals(underTest.GuardStatements);

        HashSet<Event> expectedEvents = new(expected.Premises);
        HashSet<Event> underTestEvents = new(underTest.Premises);
        bool premisesMatch = expectedEvents.SetEquals(underTestEvents);

        bool snapshotsMatch = expected.Snapshots.Equals(underTest.Snapshots);
        bool resultsMatch = expected.Result.Equals(underTest.Result);

        Debug.WriteLine($"Guards match?    {guardsMatch}\n" +
                        $"Premises match?  {premisesMatch}\n" +
                        $"Snapshots match? {snapshotsMatch}\n" +
                        $"Results match?   {resultsMatch}");
    }

    #endregion
}

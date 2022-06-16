using System;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using StatefulHorn;
using StatefulHorn.Messages;
using StatefulHorn.Parser;

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

        // In the first phase, check that basic parsing works.

        string longhandRuleSource = "know(x)(1) : {(1) :: a3} -[ (SD(init[]), a0), (SD(m), a3) : {a0 =< a3} ]-> <a3: SD(h(m, x))>";
        string shorthandRuleSource = "know(x)(a3) -[ (SD(init[]), a0), (SD(m), a3) : {a0 =< a3} ]-> <a3: SD(h(m, x))>";

        List<string> ruleSources = new(ContractExpandRuleString(longhandRuleSource));
        ruleSources.AddRange(ContractExpandRuleString(shorthandRuleSource));

        Factory.SetNextLabel("transfer-test-1");
        IMessage initNameMsg = new NameMessage("init");
        Snapshot a0 = Factory.RegisterState(new State("SD", initNameMsg));
        Snapshot a3 = Factory.RegisterState(new State("SD", new VariableMessage("m")));
        a3.SetLaterThan(a0);
        Factory.RegisterPremises(a3, Event.Know(new VariableMessage("x")));
        a3.TransfersTo = new State("SD", new FunctionMessage("h", new() { new VariableMessage("m"), new VariableMessage("x") }));
        Rule expectedRule1 = Factory.CreateStateTransferringRule();

        foreach (string src in ruleSources)
        {
            AssertRulesEqual(expectedRule1, parser.Parse(src), $"Failed to correctly parse '{src}'.");
        }

        // In the second phase, double check that the result can be correctly parsed.
        string nestedTupleSrc = "-[ (SD(<m, n>), a5) ]-> <a5: SD(<init[], init[]>)>";
        Factory.SetNextLabel("transfer-test-2");
        Snapshot a5 = Factory.RegisterState(
            new State("SD", new TupleMessage(new() { new VariableMessage("m"), new VariableMessage("n") })));
        a5.TransfersTo = new State("SD", new TupleMessage(new() { initNameMsg, initNameMsg }));
        Rule expectedNestedTuple = Factory.CreateStateTransferringRule();
        AssertRulesEqual(expectedNestedTuple, parser.Parse(nestedTupleSrc), $"Failed to correctly parse nested tuple in '{nestedTupleSrc}'");
    }

    /// <summary>
    /// Ensure that the fancy operators are parsed the same as their non-fancy counterparts.
    /// </summary>
    [TestMethod]
    public void FancyOperatorsCheck()
    {
        string plain = "-[ (SD(init[]), a0), (SD(next[]), a1), (SD(further[]), a2) : {a0 =< a1, a1 <@ a2} ]-> know(final)";
        string fancy = "-[ (SD(init[]), a0), (SD(next[]), a1), (SD(further[]), a2) : {a0 ≤ a1, a1 ⋖ a2} ]-> know(final)";

        RuleParser parser = new();
        Rule plainRule = parser.Parse(plain);
        Rule fancyRule = parser.Parse(fancy);

        Assert.AreEqual(plainRule, fancyRule, "Fancy snapshot relationship operators not parsed correctly.");
    }

    /// <summary>
    /// Check that all event name shortcuts (e.g. "k" for "know") all work.
    /// </summary>
    [TestMethod]
    public void EventShortcutsCheck()
    {
        RuleParser parser = new();
        string ruleSrcStart = "k(x), i(m), n([nonce]) -[ ]-> ";// k(t(x,m)), l(m), a(x)";
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
            Event.New(new NonceMessage("nonce"))
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
        Rule generalExpected = CreateDefaultRuleWithGuard(expectedLabel, new());

        // Note that the labels are not tested as part of rule equality, but we need to ensure
        // that the substance of the rules is correctly parsed.
        AssertRulesEqual(generalExpected, generalParsed, generalLabelInput);
        Assert.AreEqual(expectedLabel, generalParsed.Label, "Labels do not match for general case.");

        // --- With guard check ---
        string guardedLabelInput = $"{expectedLabel} = [x ~/> n[]] know(x), know(y) -[ ]-> know(enc(x, y))";
        Rule guardedParsed = parser.Parse(guardedLabelInput);
        Rule guardedExpected = CreateDefaultRuleWithGuard(expectedLabel, new() { (new VariableMessage("x"), new NameMessage("n")) });
        AssertRulesEqual(guardedExpected, guardedParsed, guardedLabelInput);
        Assert.AreEqual(expectedLabel, guardedParsed.Label, "Labels do not match for guarded case.");

        // --- Empty guard and premise check ---
        string emptyGHMInput = $"{expectedLabel} = -[ ]-> know(z[])";
        Rule emptyGHMParsed = parser.Parse(emptyGHMInput);
        Factory.SetNextLabel("expected GHM");
        Rule emptyGHMExpected = Factory.CreateStateConsistentRule(Event.Know(new NameMessage("z")));
        AssertRulesEqual(emptyGHMExpected, emptyGHMParsed, emptyGHMInput);
        Assert.AreEqual(expectedLabel, emptyGHMParsed.Label, "Labels do not match when guard and premise empty.");
    }

    /// <summary>
    /// Ensure that guard statements can be correctly parsed.
    /// </summary>
    [TestMethod]
    public void GuardChecks()
    {
        RuleParser parser = new();

        string[] parsedRules = ContractExpandRuleString("[x ~/> name[]] know(x), know(y) -[ ]-> know(enc(x, y))");

        IMessage nameMsg = new NameMessage("name");
        VariableMessage xMsg = new("x");

        Rule expected = CreateDefaultRuleWithGuard("r1", new() { (xMsg, nameMsg) });
        foreach (string src in parsedRules)
        {
            Rule generated = parser.Parse(src);
            AssertRulesEqual(expected, generated, src);
            Assert.AreEqual(1, generated.GuardStatements.Ununified.Count);
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
    private Rule CreateDefaultRuleWithGuard(string label, List<(IAssignableMessage, IMessage)> ununified)
    {
        Factory.GuardStatements = Guard.CreateFromSets(new(ununified));
        Factory.SetNextLabel(label);
        IMessage xMsg = new VariableMessage("x");
        IMessage yMsg = new VariableMessage("y");
        Factory.RegisterPremises(Event.Know(xMsg), Event.Know(yMsg));
        IMessage encMsg = new FunctionMessage("enc", new List<IMessage> { xMsg, yMsg });
        return Factory.CreateStateConsistentRule(Event.Know(encMsg));
    }

    [TestMethod]
    public void MessageParsingCheck()
    {
        State parseEx1 = PartParser.ParseState("SD(<m, right[]>)");
        State expected = new("SD", new TupleMessage(new List<IMessage>() { new VariableMessage("m"), new NameMessage("right") }));
        Assert.AreEqual(expected, parseEx1, "Unable to parse states correctly.");

        (State? parseEx2, string? _) = PartParser.TryParseState("SD(m, h[])");
        Assert.IsNull(parseEx2, "SD(m, h[]) should not parse as there are multiple contained messages.");
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
            "know(m) -[ ]-> new([n])"
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
        Rule expectedPremiseRule = CreateDefaultRuleWithGuard("premise", new());
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

using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using StatefulHorn;

namespace StatefulHornTest;

/// <summary>
/// Checks the functionality required to detect unifiable states and actually unify them.
/// </summary>
[TestClass]
public class UnificationTests
{
    private readonly RuleParser Parser = new();

    /// <summary>
    /// Basic unifiability check.
    /// </summary>
    [TestMethod]
    public void UnificationCheck()
    {
        string originalSrc = "know(mf)(a5), know(m'f)(a1) -[ " +
            "(SD(init[]), a0), (SD(h(mf, right[])), a5), (SD(init[]), a'0), (SD(h(m'f, left[])), a1) : " +
            "{ a0 =< a5, a'0 =< a1 } ]-> leak(([bobl], [bobr]))";
        string result1Src = "know(mf)(a5), know(m'f)(a1) -[ " +
            "(SD(init[]), a0), (SD(h(mf, right[])), a5), (SD(init[]), a'0), (SD(h(m'f, left[])), a1) : " +
            "{ a0 =< a5, a'0 =< a1, a0 ~ a'0 } ]-> leak(([bobl], [bobr]))";
        string result2Src = "know(mf)(a5), know(m'f)(a1) -[ " +
            "(SD(init[]), a0), (SD(h(mf, right[])), a5), (SD(init[]), a'0), (SD(h(m'f, left[])), a1) : " +
            "{ a0 =< a5, a'0 =< a1, a'0 ~ a0 } ]-> leak(([bobl], [bobr]))";

        StateConsistentRule original = Parser.ParseStateConsistentRule(originalSrc);
        StateConsistentRule resultExpected1 = Parser.ParseStateConsistentRule(result1Src);
        StateConsistentRule resultExpected2 = Parser.ParseStateConsistentRule(result2Src);

        List<Rule> unifications = original.GenerateStateUnifications();
        Assert.AreEqual(2, unifications.Count, $"Expected two rules to be returned for unification, instead returned {unifications.Count}.");
        Assert.IsTrue(unifications.Contains(resultExpected1), $"Failed to derive following rule: {resultExpected1}");
        Assert.IsTrue(unifications.Contains(resultExpected2), $"Failed to derive following rule: {resultExpected2}");
    }

    /// <summary>
    /// Ensure that that StateConsistentRule.GenerateStateUnifications is not going to just try
    /// and unify EVERY combination of states that it sees.
    /// </summary>
    [TestMethod]
    public void NonUnificationCheck()
    {
        string testSrc = "know(mf)(a1) -[ (SD(init[]), a0), (SD((mf, value[])), a1) : {a0 =< a1} ]-> leak(mf)";
        StateConsistentRule testRule = Parser.ParseStateConsistentRule(testSrc);
        List<Rule> unifications = testRule.GenerateStateUnifications();
        Assert.AreEqual(0, unifications.Count, $"Should be no valid unifications, only empty list.");
    }
}

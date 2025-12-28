using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn;
using StatefulHorn.Messages;
using StatefulHorn.Parser;
using StatefulHorn.Query;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatefulHornTest;

/// <summary>
/// Tests of the KnitPattern functionality. The KnitPattern helps to minimise the number of
/// Nessions that are generated during a query, but can be difficult to test as the 
/// relationships between the rules need to be carefully understood.
/// </summary>
[TestClass]
public class KnitTests
{

    private static readonly RuleParser Parser = new();

    /// <summary>
    /// Ensure that a group of State Transfer Rules that affect separate cells are returned
    /// together.
    /// </summary>
    [TestMethod]
    public void BasicDeconflictingKnitTest()
    {
        List<StateTransferringRule> transferRules = Parser.ParseStateTransferringRules(new string[]
        {
            "-[ (s1(a[]), a0) ]-> <a0: s1(b[])>",
            "-[ (s2(a[]), a0) ]-> <a0: s2(b[])>"
        });
        KnitPattern kp = KnitPattern.From(transferRules, new());
        List<List<StateTransferringRule>> foundGroups = kp.GetTransferGroups(DefaultNession());

        // As the two rules do not conflict, they should both be returned together.
        CheckGroupsEqualWithOutput(new() { transferRules }, foundGroups);
    }

    /// <summary>
    /// This is a test where there are two non-conflicting State Transfer Rules, but there is
    /// a State Consistent Rule that relies on an intermediate state.
    /// </summary>
    [TestMethod]
    public void FullDeconflictingKnitTest()
    {
        var stRule1 = Parser.ParseStateTransferringRule("-[ (s1(a[]), a0) ]-> <a0: s1(b[])>");
        var stRule2 = Parser.ParseStateTransferringRule("-[ (s2(a[]), a0) ]-> <a0: s2(b[])>");
        List<StateTransferringRule> transferRules = new() { stRule1, stRule2 };
        List<StateConsistentRule> consistRules = Parser.ParseStateConsistentRules(new string[]
        {
            "k(a)(a0) -[ (s1(a[]), a0), (s2(b[]), b0) ]-> k(h(a))"
        });
        KnitPattern kp = KnitPattern.From(transferRules, consistRules);
        List<List<StateTransferringRule>> foundGroups = kp.GetTransferGroups(DefaultNession());

        List<List<StateTransferringRule>> expectedGroups = new() 
        { 
            new() { stRule1 }, 
            new() { stRule2 } 
        };

        CheckGroupsEqualWithOutput(expectedGroups, foundGroups);
    }

    /// <summary>
    /// Tests a case where there are two State Transfer Rules that require the same 
    /// matching set of states to mutate, but adjust each state cell separately.
    /// </summary>
    [TestMethod]
    public void FrameDeconflictingKnitTest()
    {
        List<StateTransferringRule> transferRules = Parser.ParseStateTransferringRules(new string[]
        {
            "-[ (s1(a[]), a0), (s2(a[]), b0) ]-> <a0: s1(b[])>",
            "-[ (s1(a[]), a0), (s2(a[]), b0) ]-> <b0: s2(b[])>"
        });
        KnitPattern kp = KnitPattern.From(transferRules, new());
        List<List<StateTransferringRule>> foundGroups = kp.GetTransferGroups(DefaultNession());

        // As the two rules do not conflict, they should both be returned together.
        CheckGroupsEqualWithOutput(new() { transferRules }, foundGroups);
    }

    #region Convenience methods.

    private static Nession DefaultNession()
    {
        List<State> initStates = new()
        {
            new("s1", new NameMessage("a")),
            new("s2", new NameMessage("a"))
        };
        return new(initStates);
    }

    private static void CheckGroupsEqualWithOutput(
        List<List<StateTransferringRule>> expected,
        List<List<StateTransferringRule>> found)
    {
        try
        {
            TestGroupsEqual(expected, found);
        }
        catch (Exception)
        {
            Console.WriteLine("=== Expected rule groupings ===");
            OutputGroupings(expected);
            Console.WriteLine("=== Found rule groupings ===");
            OutputGroupings(found);
            throw;
        }
    }

    private static void TestGroupsEqual(
        List<List<StateTransferringRule>> expected,
        List<List<StateTransferringRule>> found)
    {
        Assert.HasCount(expected.Count, found, "Groups not equal");

        List<List<StateTransferringRule>> scratchFound = new(found);
        for (int i = 0; i < expected.Count; i++)
        {
            ISet<StateTransferringRule> ruleSet = expected[i].ToHashSet();
            bool foundMatch = false;
            for (int j = 0; j < scratchFound.Count; j++)
            {
                if (ruleSet.SetEquals(scratchFound[j]))
                {
                    foundMatch = true;
                    scratchFound.RemoveAt(j);
                    break;
                }
            }
            if (!foundMatch)
            {
                Assert.Fail("Groups do not match.");
            }
        }
    }

    private static void OutputGroupings(List<List<StateTransferringRule>> ruleGroupings)
    {
        for (int i = 0; i < ruleGroupings.Count; i++)
        {
            foreach (StateTransferringRule str in ruleGroupings[i])
            {
                Console.WriteLine(str);
            }
            if (i < ruleGroupings.Count - 1)
            {
                Console.WriteLine("----------------");
            }
        }
    }

    #endregion

}

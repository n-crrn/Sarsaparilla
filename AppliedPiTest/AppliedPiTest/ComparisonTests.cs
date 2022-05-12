using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using AppliedPi;
using AppliedPi.Model;
using AppliedPi.Model.Comparison;
using AppliedPi.Translate;
using StatefulHorn;
using StatefulHorn.Messages;

namespace SarsaparillaTests.AppliedPiTest;

[TestClass]
public class ComparisonTests
{
    #region General comparison operations tests.

    [TestMethod]
    public void ConstructionTest()
    {
        IComparison expectedCmp1 = new BooleanComparison(
            BooleanComparison.Type.And,
            new EqualityComparison(true, "b", "c"),
            new EqualityComparison(false, "d", "e"));
        DoConstructTest(new() { "b", "=", "c", "&&", "d", "<>", "e" }, expectedCmp1);

        IComparison expectedCmp2 = new BooleanComparison(
            BooleanComparison.Type.Or,
            new BooleanComparison(
                BooleanComparison.Type.And,
                new EqualityComparison(false, "b", "c"),
                new EqualityComparison(true, "d", "e")
                ),
            new EqualityComparison(true, "a", "b"));
        DoConstructTest(new() { "(", "b", "<>", "c", "&&", "d", "=", "e", ")", "||", "a", "=", "b" }, expectedCmp2);

        IComparison expectedCmp3 = new NotComparison(new IsComparison("a"));
        //DoConstructTest(new() { "not", "(", "a", ")" }, expectedCmp3);
        DoConstructTest(new() { "not", "a" }, expectedCmp3);

        IComparison expectedCmp4 = new BooleanComparison(
            BooleanComparison.Type.Or,
            new NotComparison(new BooleanComparison(BooleanComparison.Type.And, "a", "b")),
            new IsComparison("c"));
        DoConstructTest(new() { "not", "(", "a", "&&", "b", ")", "||", "c" }, expectedCmp4);
    }

    private static void DoConstructTest(List<string> tokens, IComparison expectedCmp)
    {
        IComparison parsedCmp = ComparisonParser.Parse(tokens);
        Assert.AreEqual(expectedCmp, parsedCmp);
    }

    [TestMethod]
    public void TypeTest()
    {
        // Resolver for the tests.
        TermResolver tr = new(new());
        tr.Register(new("bsA"), new(TermSource.Free, PiType.BitString));
        tr.Register(new("bsB"), new(TermSource.Free, PiType.BitString));
        tr.Register(new("boolC"), new(TermSource.Free, PiType.Bool));

        // Correct examples.
        IComparison cmp1 = new BooleanComparison(
            BooleanComparison.Type.And,
            new EqualityComparison(true, "bsA", "bsB"),
            new IsComparison("boolC"));
        Assert.AreEqual(PiType.Bool, cmp1.ResolveType(tr));

        IComparison cmp2 = new NotComparison(new EqualityComparison(false, "bsA", "bsB"));
        Assert.AreEqual(PiType.Bool, cmp2.ResolveType(tr));

        // Incorrect examples.
        IComparison cmp3 = new BooleanComparison(BooleanComparison.Type.And, "bsA", "boolC");
        Assert.IsNull(cmp3.ResolveType(tr));

        IComparison cmp4 = new EqualityComparison(true, "bsA", "boolC");
        Assert.IsNull(cmp4.ResolveType(tr));
    }

    [TestMethod]
    public void PositiviseTest()
    {
        IComparison expectedCmp = new BooleanComparison(
            BooleanComparison.Type.And,
            new EqualityComparison(true, "b", "c"),
            new EqualityComparison(false, "d", "e"));
        IComparison postived = expectedCmp.Positivise();
        Assert.AreEqual(expectedCmp, postived, "Comparison with no 'not' clauses modified.");

        IComparison negativeCmp = new NotComparison(expectedCmp);
        IComparison negExpectedCmp = new BooleanComparison(
            BooleanComparison.Type.Or,
            new EqualityComparison(false, "b", "c"),
            new EqualityComparison(true, "d", "e"));
        Assert.AreEqual(negExpectedCmp, negativeCmp.Positivise(), "Top-level negative incorrectly handled.");

        IComparison simpleBooleanCmp = new NotComparison(new NotComparison(new IsComparison("A")));
        IComparison sbExpectedCmp = new IsComparison("A");
        Assert.AreEqual(sbExpectedCmp, simpleBooleanCmp.Positivise(), "Negative clauses not removed.");
    }

    #endregion
    #region Branch restriction determination tests.

    [TestMethod]
    public void BasicBranchRestrictionsTest()
    {
        Dictionary<Term, TermOriginRecord> termDetails = new()
        {
            { new("x"), new(TermSource.Input, PiType.Bool) },
            { new("y"), new(TermSource.Input, PiType.Bool) }
        };
        VariableMessage x = new("x");
        VariableMessage y = new("y");
        SigmaMap expectedSigma = new(y, x);
        Guard expectedGuard = new(
            new Dictionary<IAssignableMessage, HashSet<IMessage>>()
            {
                { x, new() { y } },
                { y, new() { x } }
            });
        List<(SigmaMap, Guard)> expectedIfs = new() { (expectedSigma, Guard.Empty) };
        List<(SigmaMap, Guard)> expectedElses = new() { (SigmaMap.Empty, expectedGuard) };
        IComparison basicEqualCmp = new EqualityComparison(true, "x", "y");
        IComparison basicNotEqualCmp = new EqualityComparison(false, "x", "y");

        TestRestrictions(termDetails, new(), basicEqualCmp, expectedIfs, expectedElses);
        TestRestrictions(termDetails, new(), basicNotEqualCmp, expectedElses, expectedIfs);
    }

    [TestMethod]
    public void BooleanBranchRestrictionsTest()
    {
        Dictionary<Term, TermOriginRecord> termDetails = new()
        {
            { new("C"), new(TermSource.Free, PiType.Channel) },
            { new("d"), new(TermSource.Input, PiType.Channel) },
            { new("E"), new(TermSource.Free, PiType.BitString) },
            { new("f"), new(TermSource.Input, PiType.BitString) }
        };
        NameMessage c = new("C");
        VariableMessage d = new("d");
        NameMessage e = new("E");
        VariableMessage f = new("f");

        // Test the expression C == d && E == f.
        IComparison expr1 = new BooleanComparison(
            BooleanComparison.Type.And,
            new EqualityComparison(true, "C", "d"),
            new EqualityComparison(true, "E", "f"));
        SigmaMap expectedExpr1Sm = new(new() { (d, c), (f, e) });
        List<(SigmaMap, Guard)> expectedExpr1Ifs = new() { (expectedExpr1Sm, Guard.Empty) };
        List<(SigmaMap, Guard)> expectedExpr1Elses = new()
        { 
            (SigmaMap.Empty, new(d, c)),
            (SigmaMap.Empty, new(f, e))
        };
        TestRestrictions(termDetails, new(), expr1, expectedExpr1Ifs, expectedExpr1Elses);

        // Test the expression !(C != d) || (E != f)), which should translate back to the same thing.
        IComparison expr2 = new NotComparison(
            new BooleanComparison(
                BooleanComparison.Type.Or,
                new EqualityComparison(false, "C", "d"),
                new EqualityComparison(false, "E", "f")
            ) );
        TestRestrictions(termDetails, new(), expr2, expectedExpr1Ifs, expectedExpr1Elses);
    }

    private static void TestRestrictions(
        Dictionary<Term, TermOriginRecord> termDetails,
        HashSet<Destructor> destructor,
        IComparison comp,
        List<(SigmaMap, Guard)> expectedIfs,
        List<(SigmaMap, Guard)> expectedElse)
    {
        Network nw = Network.DirectCreate(destructor);
        ResolvedNetwork rn = new();
        rn.DirectSet(termDetails, new());

        BranchRestrictionSet brs = BranchRestrictionSet.From(comp, rn, nw);
        List<(SigmaMap, Guard)> ifConds = brs.OutputIfBranchConditions();
        List<(SigmaMap, Guard)> elseConds = brs.OutputElseBranchConditions();

        Assert.IsTrue(ConditionsMatch(expectedIfs, ifConds));
        Assert.IsTrue(ConditionsMatch(expectedElse, elseConds));
    }

    private static bool ConditionsMatch(List<(SigmaMap, Guard)> expected, List<(SigmaMap, Guard)> found)
    {
        bool[] foundFound = new bool[found.Count];

        List<(SigmaMap, Guard)> missingExpected = new();
        foreach ((SigmaMap expSm, Guard expG) in expected)
        {
            bool foundExpected = false;
            for (int i = 0; i < found.Count && !foundExpected; i++)
            {
                if (!foundFound[i])
                {
                    (SigmaMap foundSm, Guard foundG) = found[i];
                    foundExpected = expSm.Equals(foundSm) && expG.Equals(foundG);
                    foundFound[i] |= foundExpected;
                }
            }
            if (!foundExpected)
            {
                missingExpected.Add((expSm, expG));
            }
        }

        bool succeeded = true;

        // Output any differences found so that the bug can be chased.
        if (missingExpected.Count > 0)
        {
            succeeded = false;
            Console.WriteLine("Following expected restrictions were not found:");
            foreach ((SigmaMap sm, Guard g) in missingExpected)
            {
                Console.WriteLine($"  Replace {sm}, ban {g}");
            }
        }
        bool foundNotFound = (from f in foundFound where !f select f).Any();
        if (foundNotFound) 
        {
            succeeded = false;
            Console.WriteLine("Following results were found but not expected:");
            for (int i = 0; i < foundFound.Length; i++)
            {
                if (!foundFound[i])
                {
                    (SigmaMap ffSm, Guard ffG) = found[i];
                    Console.WriteLine($"  Replace {ffSm}, ban {ffG}");
                }
            }
        }

        return succeeded;
    }

    #endregion
}

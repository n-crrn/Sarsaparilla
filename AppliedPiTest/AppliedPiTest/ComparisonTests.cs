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

    [TestMethod]
    public void BasicBranchRestrictionsTest()
    {
        ResolvedNetwork rn = new();
        Dictionary<Term, TermOriginRecord> termDetails = new()
        {
            { new("x"), new(TermSource.Input, PiType.Bool) },
            { new("y"), new(TermSource.Input, PiType.Bool) }
        };
        rn.DirectSet(termDetails, new());
        IComparison basicEqualCmp = new EqualityComparison(true, "x", "y");
        IComparison basicNotEqualCmp = new EqualityComparison(false, "x", "y");

        VariableMessage x = new("x");
        VariableMessage y = new("y");
        SigmaMap expectedSigma1 = new(x, y); // The result should be one or the
        SigmaMap expectedSigma2 = new(y, x); // other of these two options.
        Guard expectedGuard1 = new(
            new Dictionary<IAssignableMessage, HashSet<IMessage>>() 
            { 
                { x, new() { y } },
                { y, new() {x} }
            } );

        // First sub-test - does the equality result make sense?
        BranchRestrictionSet eqBrSet = BranchRestrictionSet.From(basicEqualCmp, rn, new());
        (SigmaMap smEqIf, Guard gdEqIf) = eqBrSet.OutputIfBranchConditions().Single();
        (SigmaMap smEqElse, Guard gdEqElse) = eqBrSet.OutputElseBranchConditions().Single();

        Assert.AreEqual(Guard.Empty, gdEqIf, "If condition guard expected to be empty.");
        Assert.IsTrue(expectedSigma1.Equals(smEqIf) || expectedSigma2.Equals(smEqIf), "Different sigma replacement map found.");
        Assert.IsTrue(smEqElse.IsEmpty, "Else condition sigma map expected to be empty.");
        Assert.AreEqual(expectedGuard1, gdEqElse, $"Else guards do not match (found {gdEqElse}).");

        // Second sub-test - does the inequality result make sense?
        BranchRestrictionSet neqBrSet = BranchRestrictionSet.From(basicNotEqualCmp, rn, new());
        (SigmaMap smNeqIf, Guard gdNeqIf) = neqBrSet.OutputIfBranchConditions().Single();
        (SigmaMap smNeqElse, Guard gdNeqElse) = neqBrSet.OutputElseBranchConditions().Single();

        Assert.IsTrue(smNeqIf.IsEmpty, "If condition expected to be empty.");
        Assert.AreEqual(expectedGuard1, gdNeqIf, "Guard for if condition not as expected.");
        Assert.IsTrue(expectedSigma1.Equals(smNeqElse) || expectedSigma2.Equals(smNeqElse), "Else condition sigma map does not match expected.");
        Assert.AreEqual(Guard.Empty, gdNeqElse, "Else condition guard not empty.");
    }

    
}

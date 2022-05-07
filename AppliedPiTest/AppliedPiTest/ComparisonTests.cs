using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using AppliedPi;
using AppliedPi.Model;
using AppliedPi.Model.Comparison;

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

    
}

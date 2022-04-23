using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using AppliedPi.Model;

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
}

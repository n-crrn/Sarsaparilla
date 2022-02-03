using Microsoft.VisualStudio.TestTools.UnitTesting;
using StatefulHorn;

namespace StatefulHornTest;

/// <summary>
/// Tests to ensure that state transformation works as intended.
/// </summary>
[TestClass]
public class TransformationTests
{

    private readonly RuleParser Parser = new();

    #region Old Transformation Code.

    [TestMethod]
    public void BasicTransformationTest()
    {
        string translateSrc = "k(x)(a0) -[ (SD(init[]), a0) ]-> <a0: SD(x)>";
        string opSrc = "k(test[])(a0) -[ (SD(init[]), a0) ]-> k(enc(test[]))";
        string expectedSrc = "k(test[])(a0) -[ (SD(init[]), a0), (SD(test[]), a1) : {a0 <@ a1} ]-> k(enc(test[]))";
        DoTransformationTest(translateSrc, opSrc, expectedSrc);
    }

    [TestMethod]
    public void AdvancedTransformationTest()
    {
        // The following was found to be defective as of 2022-01-12.
        string translateSrc2 = "-[ (SD(init[]), a0), (SD(m), a1) : {a0 =< a1} ]-> <a1: SD(h(m, left[]))>";
        string opSrc2 = "know(aenc(<mf, sl, sk>, pk(sksd[])))(1), know(aenc(m, pk(sk)))(2) : {(1) :: a1, (2) :: a1} " + 
            "-[ (SD(init[]), a0), (SD(h(mf, right[])), a1) : {a0 =< a1} ]-> know(m)";
        string expectedSrc2 = "know(aenc(<mf, sl, sk>, pk(sksd[])))(1), know(aenc(m, pk(sk)))(2) : {(1) :: a1, (2) :: a1} " + 
            "-[ (SD(init[]), a0), (SD(h(mf, right[])), a1), (SD(h(h(mf, right[]), left[])), a2) : {a0 =< a1, a1 <@ a2} ]-> " + 
            "know(m)";
        DoTransformationTest(translateSrc2, opSrc2, expectedSrc2);
    }

    #endregion

    [TestMethod]
    public void UpdatedTransformationTest()
    {
        string transformSrc = "k(x)(d1) -[ (SD(init[]), d0), (SD(m), d1) : { d0 =< d1} ]-> <d1: SD(h(m, x))>";
        string opSrc = "-[ (SD(init[]), c0), (SD(m), c1) : { c0 =< c1} ]-> k(m)";
        string expectedSrc = "k(x)(a1) -[ (SD(init[]), a0), (SD(m), a1), (SD(h(m, x)), a2) : {a0 =< a1, a1 <@ a2} ]-> k(m)";
        DoUpdatedTransformationTest(transformSrc, opSrc, expectedSrc);
    }

    private void DoTransformationTest(string transformSrc, string opSrc, string expectedSrc)
    {
        StateTransferringRule transferRule = Parser.ParseStateTransferringRule(transformSrc);
        StateConsistentRule opRule = Parser.ParseStateConsistentRule(opSrc);
        StateConsistentRule expectedRule = Parser.ParseStateConsistentRule(expectedSrc);

        StateConsistentRule? transformedRule = transferRule.Transform(opRule);
        Assert.AreEqual(expectedRule, transformedRule, "Transformed rule not as expected.");
    }

    private void DoUpdatedTransformationTest(string transformSrc, string opSrc, string expectedSrc)
    {
        StateTransferringRule transferRule = Parser.ParseStateTransferringRule(transformSrc);
        StateConsistentRule opRule = Parser.ParseStateConsistentRule(opSrc);
        StateConsistentRule expectedRule = Parser.ParseStateConsistentRule(expectedSrc);

        StateConsistentRule? transformedRule = transferRule.TryTransform(opRule);
        Assert.AreEqual(expectedRule, transformedRule, "Transformed rule not as expected.");
    }

}

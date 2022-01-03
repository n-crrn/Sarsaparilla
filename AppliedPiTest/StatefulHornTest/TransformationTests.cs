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
        string translateSrc = "k(x)(a3) -[ (SD(init[]), a0), (SD(m), a3) : {a0 =< a3} ]-> <a3: SD(h(m,x))>";
        string opSrc = "n([bobl], lsl[])(a5), k(mf)(a5) -[ (SD(init[]), a0), (SD(h(mf, right[])), a5) : {a0 =< a5} ]-> leak(bobl)";
        string expectedSrc = "n([bobl], lsl[])(a5), k(mf)(a5) -[ (SD(init[]), a0), (SD(h(mf, right[])), a5), (SD(h(h(mf, right[]), mf)), a6) : {a0 =< a5, a5 <@ a6} ]-> leak(bobl)";
        DoTransformationTest(translateSrc, opSrc, expectedSrc);
    }

    private void DoTransformationTest(string transformSrc, string opSrc, string expectedSrc)
    {
        StateTransferringRule transferRule = Parser.ParseStateTransferringRule(transformSrc);
        StateConsistentRule opRule = Parser.ParseStateConsistentRule(opSrc);
        StateConsistentRule expectedRule = Parser.ParseStateConsistentRule(expectedSrc);

        StateConsistentRule? transformedRule = transferRule.Transform(opRule);
        Assert.AreEqual(expectedRule, transformedRule, "Transformed rule not as expected.");
    }

}

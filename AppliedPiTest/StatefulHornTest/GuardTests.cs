using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using StatefulHorn;
using StatefulHorn.Messages;

namespace StatefulHornTest;

[TestClass]
public class GuardTests
{
    [TestMethod]
    public void BasicUnifiedToGuardTest()
    {
        Guard basicGuard = Guard.CreateFromSets(new() { (new VariableMessage("x"), new NameMessage("a")) });
        IMessage toMessage = new FunctionMessage("f1", new() { new VariableMessage("x"), new VariableMessage("y") });

        IMessage fromMessage1 = new FunctionMessage("f1", new() { new NameMessage("a"), new NameMessage("b") });
        IMessage fromMessage2 = new FunctionMessage("f1", new() { new NameMessage("b"), new NameMessage("a") });

        DoUnifiedToTest(Guard.Empty, fromMessage1, toMessage, true, "Empty guard failed to pass substitution.");
        DoUnifiedToTest(basicGuard, fromMessage1, toMessage, false, "Guard failed to protect message.");
        DoUnifiedToTest(basicGuard, fromMessage2, toMessage, true, "Guard failed to pass valid substitution.");
    }

    [TestMethod]
    public void VariableEqualGuardTest()
    {
        Guard basicGuard = Guard.CreateFromSets(new() { (new VariableMessage("x"), new VariableMessage("y")) });
        IMessage toMessage = new FunctionMessage("f2", new() { new VariableMessage("x"), new VariableMessage("y") });

        IMessage fromMessage1 = new FunctionMessage("f2", new() { new NameMessage("a"), new NameMessage("b") });
        IMessage fromMessage2 = new FunctionMessage("f2", new() { new NameMessage("a"), new NameMessage("a") });

        DoUnifiedToTest(basicGuard, fromMessage2, toMessage, false, "Guard failed to protect message.");
        DoUnifiedToTest(basicGuard, fromMessage1, toMessage, true, "Guard failed to pass valid substitution.");
        DoUnifiedToTest(Guard.Empty, fromMessage2, toMessage, true, "Empty guard failed to pass substitution.");
    }

    private static void DoUnifiedToTest(Guard g, IMessage from, IMessage to, bool expectedSucceed, string failMsg)
    {
        SigmaFactory sf = new();
        bool succeed = to.DetermineUnifiedToSubstitution(from, g, sf);
        Assert.AreEqual(expectedSucceed, succeed, failMsg);
    }

    [TestMethod]
    public void EmptyGuardTest()
    {
        IMessage check = new FunctionMessage("dec", new()
        {
            new FunctionMessage("enc",
                                new()
                                {
                                    new VariableMessage("x"),
                                    new VariableMessage("y")
                                })
        });
        IMessage query = new FunctionMessage("dec", new()
        {
            new FunctionMessage("enc",
                                new()
                                {
                                    new NameMessage("value"),
                                    new VariableMessage("y")
                                })
        });
        SigmaFactory sf = new();
        bool succeed = check.DetermineUnifiableSubstitution(query, Guard.Empty, Guard.Empty, sf);
        Assert.IsTrue(succeed, "Failed to find unifiable substitution with empty guard.");
    }

    [TestMethod]
    public void TupleFunctionGuardTest()
    {
        IMessage hashAMsg = new FunctionMessage("h", new() { new NameMessage("A") });
        IMessage bMsg = new NameMessage("B");
        IAssignableMessage mfMsg = new VariableMessage("mf");
        IMessage xMsg = new VariableMessage("x");
        
        IMessage tuple1Msg = new TupleMessage(new List<IMessage>() { hashAMsg, bMsg });
        IMessage tuple2Msg = new TupleMessage(new List<IMessage>() { mfMsg, xMsg });

        Guard g = new(mfMsg, hashAMsg);
        const string mfxCell = "mf@x@cell";
        IMessage testMsg1 = new FunctionMessage(mfxCell, new List<IMessage>() { tuple1Msg });
        IMessage testMsg2 = new FunctionMessage(mfxCell, new List<IMessage>() { tuple2Msg });
        Assert.IsFalse(testMsg2.DetermineUnifiedToSubstitution(testMsg1, g, new()));
    }

}

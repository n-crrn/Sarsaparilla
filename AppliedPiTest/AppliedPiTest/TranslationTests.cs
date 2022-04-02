using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using AppliedPi;
using StatefulHorn;
using StatefulHorn.Messages;

namespace SarsaparillaTests.AppliedPiTest;

[TestClass]
public class TranslationTests
{

    private static void DoTest(StatefulHornTranslation expectedSht, Network nw)
    {
        //StatefulHornTranslation sht = StatefulHornTranslator.Translate(nw);
        (StatefulHornTranslation? sht, string? err) = StatefulHornTranslation.Translate(nw);
        Assert.IsNull(err);
        Assert.IsNotNull(sht);
        try
        {
            Assert.AreEqual(expectedSht, sht, "Translation for constructors/destructors failed.");
        }
        catch (Exception)
        {
            Console.WriteLine("--- Expected translation ---");
            expectedSht.Describe(Console.Out);
            Console.WriteLine("--- Generated translation ---");
            sht.Describe(Console.Out);
            throw;
        }
    }

    [TestMethod]
    public void ConstantAndFreeTests()
    {
        // Sample input.
        string testSource =
            "free A, B: host.\n" +
            "free C: host [private].\n" +
            "const D: host.";
        Network nw = Network.CreateFromCode(testSource);

        // Create expected output for comparison.
        RuleFactory factory = new();
        StatefulHornTranslation expectedSht = new();
        foreach (string name in new string[] { "A", "B", "D" })
        {
            expectedSht.Rules.Add(factory.CreateStateConsistentRule(Event.Know(new NameMessage(name))));
        }

        // Testing and checking.
        DoTest(expectedSht, nw);
    }

    [TestMethod]
    public void ConstructorTests()
    {
        string testSource = 
            "fun pk(skey): pkey.\n" +
            "reduc forall x: bitstring, y: skey; decrypt(encrypt(x, y), y) = x.";
        Network nw = Network.CreateFromCode(testSource);

        // Create expected output for comparison.
        RuleFactory factory = new();
        StatefulHornTranslation expectedSht = new();
        factory.RegisterPremise(Event.Know(new VariableMessage("skey")));
        expectedSht.Rules.Add(factory.CreateStateConsistentRule(Event.Know(MessageParser.ParseMessage("pk(skey)"))));
        factory.RegisterPremise(Event.Know(MessageParser.ParseMessage("decrypt(encrypt(x, y), y)")));
        expectedSht.Rules.Add(factory.CreateStateConsistentRule(Event.Know(new VariableMessage("x"))));

        // Testing and checking.
        DoTest(expectedSht, nw);
    }

}

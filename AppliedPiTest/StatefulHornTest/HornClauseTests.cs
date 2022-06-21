using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StatefulHorn;
using StatefulHorn.Messages;
using StatefulHorn.Parser;
using StatefulHorn.Query;

namespace StatefulHornTest;

[TestClass]
public class HornClauseTests
{
    #region Tests.

    [TestMethod]
    public void ComposeTest()
    {
        VariableMessage xMsg = new("x");
        VariableMessage x13Msg = new("x_13");
        NameMessage sMsg = new("s");
        DoComposeTest((-1, new() { xMsg }, new FunctionMessage("SD@x", new() { xMsg })),
               (2, new() { new FunctionMessage("SD@x", new() { x13Msg }) }, sMsg),
               (2, new() { x13Msg }, sMsg),
               "Basic compose");

        // More complex example.
        DoComposeTest((-1, new() { xMsg }, new FunctionMessage("SD@x", new() { xMsg })),
               (2, ParseMessages("BobSDSet@b[]", "SD@x(SD@x_v13)", "SD@mUpdated@cell(h(SD@mStart[], left[]))",
                   "SD@enc_rx@cell(SD@enc_rx-v13)", "SD@mf@SD@sl@SD@sr@cell(<SD@mf_v13, SD@sl_v13, SD@sr_v13>)"),
                   PartParser.ParseMessage("SD@sl_v13")),
               (2, ParseMessages("BobSDSet@b[]", "SD@x_v13", "SD@mUpdated@cell(h(SD@mStart[], left[]))",
                   "SD@enc_rx@cell(SD@enc_rx-v13)", "SD@mf@SD@sl@SD@sr@cell(<SD@mf_v13, SD@sl_v13, SD@sr_v13>)"),
                   PartParser.ParseMessage("SD@sl_v13")),
               "Advanced compose");

        // Multiple replace - number one.
        DoComposeTest((-1, new() { xMsg }, new FunctionMessage("SD@x", new() { xMsg })),
               (2, ParseMessages("BobSDSet@b[]", "SD@x(SD@x_v13)", "SD@x(Name[])",
                   "SD@enc_rx@cell(SD@enc_rx-v13)", "SD@mf@SD@sl@SD@sr@cell(<SD@mf_v13, SD@sl_v13, SD@sr_v13>)"),
                   PartParser.ParseMessage("SD@sl_v13")),
               (2, ParseMessages("BobSDSet@b[]", "SD@x_v13", "Name[]",
                   "SD@enc_rx@cell(SD@enc_rx-v13)", "SD@mf@SD@sl@SD@sr@cell(<SD@mf_v13, SD@sl_v13, SD@sr_v13>)"),
                   PartParser.ParseMessage("SD@sl_v13")),
               "Advanced multi-compose 1");

        // Multiple replace - number two.
        DoComposeTest((-1, new() { xMsg }, new FunctionMessage("SD@x@cell", new() { xMsg })),
               (-1, ParseMessages("@BobSDSet@b@0[]", "SD@x@cell(SD@x)", "SD@enc_rx@cell(SD@enc_rx)",
                "SD@x@cell(left[])", "SD@mUpdated@cell(SD@mUpdated)",
                "SD@mf@SD@sl@SD@sr@cell(dec(enc(<SD@mf, SD@sl, SD@sr>, pk(y)), y))"),
                PartParser.ParseMessage("SD@sl")),
               (-1, ParseMessages("@BobSDSet@b@0[]", "SD@x", "SD@enc_rx@cell(SD@enc_rx)",
                "left[]", "SD@mUpdated@cell(SD@mUpdated)",
                "SD@mf@SD@sl@SD@sr@cell(dec(enc(<SD@mf, SD@sl, SD@sr>, pk(y)), y))"),
                PartParser.ParseMessage("SD@sl")),
               "Advanced multi-compose 2");

        // Self refential risk.
        DoComposeTest((-1, ParseMessages("@BobSDSet@b@0[]", "SD@x@cell(SD@x)", "SD@mUpdated@cell(SD@mUpdated)",
                "SD@enc_rx@cell(SD@enc_rx)", "SD@mf@SD@sl@SD@sr@cell(dec(enc(x, pk(y)), y))"),
                PartParser.ParseMessage("SD@mf@SD@sl@SD@sr@cell(x)")),
                (-1, ParseMessages("@BobSDSet@b@0[]", "SD@x@cell(SD@x)", "SD@enc_rx@cell(SD@enc_rx)",
                "SD@mf@SD@sl@SD@sr@cell(<SD@mf, SD@sl, SD@sr>)"),
                PartParser.ParseMessage("SD@sl")),
                (-1, ParseMessages("@BobSDSet@b@0[]", "SD@x@cell(SD@x)", "SD@mUpdated@cell(SD@mUpdated)",
                "SD@enc_rx@cell(SD@enc_rx)", "SD@mf@SD@sl@SD@sr@cell(dec(enc(<SD@mf, SD@sl, SD@sr>, pk(y)), y))"),
                PartParser.ParseMessage("SD@sl")),
                "Self-referential compose");

        // Nil premise match.
        DoComposeTest((-1, new(), PartParser.ParseMessage("@BobSDSet@b@0[]")),
                (-1, ParseMessages("@BobSDSet@b@0[]", "SD@x@cell(SD@x)", "SD@enc_rx@cell(SD@enc_rx)",
                "SD@mf@SD@sl@SD@sr@cell(<SD@mf, SD@sl, SD@sr>)"),
                PartParser.ParseMessage("SD@sl")),
                (-1, ParseMessages("SD@x@cell(SD@x)", "SD@enc_rx@cell(SD@enc_rx)", 
                "SD@mf@SD@sl@SD@sr@cell(<SD@mf, SD@sl, SD@sr>)"),
                PartParser.ParseMessage("SD@sl")),
                "Nil premise compose");
    }

    [TestMethod]
    public void ImplyTest()
    {
        HornClause hc1_1 = MakeClause(1, new(), PartParser.ParseMessage("test[]"));
        HornClause hc1_2 = MakeClause(1, ParseMessages("another[]", "testing[]"), PartParser.ParseMessage("test[]"));
        Assert.IsTrue(hc1_1.Implies(hc1_2), "Failed on empty premised implier.");

        HornClause hc2_1 = MakeClause(-1, ParseMessages("f(x, y)", "y"), PartParser.ParseMessage("x"));
        HornClause hc2_2 = MakeClause(-1, ParseMessages("f(x, key[])", "key[]"), PartParser.ParseMessage("x"));
        Assert.IsTrue(hc2_1.Implies(hc2_2), "Failed on function implier.");
    }

    [TestMethod]
    public void GuardFilterTest()
    {
        Guard g = new(new VariableMessage("x"), new NameMessage("A"));
        List<IMessage> constMsgs = new() { new NameMessage("A"), new NameMessage("B") };
        HornClause hc1 = new(new NameMessage("C"), constMsgs, g);
        Assert.IsTrue(hc1.Guard.IsEmpty, "Guard not filtered.");
    }

    #endregion
    #region Convenience methods.

    private static List<IMessage> ParseMessages(params string[] msgs)
    {
        return new(from m in msgs select PartParser.ParseMessage(m));
    }

    private static void DoComposeTest(
        (int, List<IMessage>, IMessage) clause1, 
        (int, List<IMessage>, IMessage) clause2,
        (int, List<IMessage>, IMessage) expectedResult,
        string errMessage)
    {
        HornClause hc1 = MakeClause(clause1.Item1, clause1.Item2, clause1.Item3);
        HornClause hc2 = MakeClause(clause2.Item1, clause2.Item2, clause2.Item3);
        HornClause expected = MakeClause(expectedResult.Item1, expectedResult.Item2, expectedResult.Item3);

        HornClause? found = null;
        try
        {
            found = hc1.ComposeUpon(hc2);
            Assert.IsNotNull(found);
            Assert.AreEqual(found, expected, errMessage);
        } 
        catch (Exception)
        {
            Debug.WriteLine($"Clause 1: {hc1}");
            Debug.WriteLine($"Clause 2: {hc2}");
            Debug.WriteLine($"Expected: {expected}");
            if (found != null)
            {
                Debug.WriteLine($"Produced: {found}");
            }
            else
            {
                Debug.WriteLine($"No result.");
            }
            throw;
        }
    }

    private static HornClause MakeClause(int rank, List<IMessage> premises, IMessage result)
    {
        return new(result, premises)
        {
            Rank = rank
        };
    }

    #endregion

}

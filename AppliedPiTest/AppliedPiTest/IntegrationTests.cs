using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StatefulHorn;
using AppliedPi;
using AppliedPi.Translate;

namespace SarsaparillaTests.AppliedPiTest;

/// <summary>
/// Tests for the complete chain of Applied Pi through Stateful Horn through querying.
/// </summary>
[TestClass]
public class IntegrationTests
{

    [TestMethod]
    public async Task BasicChainTest()
    {
        string piSource =
@"free c: channel.
free s: bitstring [private].
query attacker(s).
process ( out(c, s) | in(c, v: bitstring) ).
";
        await DoTest(piSource, false, true);
    }

    [TestMethod]
    public async Task ValueTransferTest()
    {
        string piSource =
@"free c: channel.
free d: channel [private].
free s: bitstring [private].

query attacker(s).

process
  out(d, s) | ( in(d, v: bitstring); out(c, v) ).
";
        await DoTest(piSource, false, true);
    }

    [TestMethod]
    public async Task FalseAttackAvoidanceTest()
    {
        string piSource =
@"free c: channel.
free d: channel [private].
free s: bitstring [private].

query attacker(s).

process
  out(d, s) | ( in(d, v: bitstring); out(c, d) ).
";
        await DoTest(piSource, false, false);
    }

    private async Task DoTest(string src, bool expectGlobalAttack, bool expectNessionAttack)
    {
        Network nw = Network.CreateFromCode(src);
        ResolvedNetwork rn = ResolvedNetwork.From(nw);
        Translation t = Translation.From(rn, nw);

        try
        {
            Assert.AreEqual(1, t.Queries.Count);
            QueryEngine qe = t.QueryEngines().First();

            // Preparations for running the query engine.
            bool globalAttackFound = false;
            void onGlobalAttackFound(Attack a) => globalAttackFound = true;
            bool nessionAttackFound = false;
            void onAttackAssessedFound(Nession n, HashSet<HornClause> hs, Attack? a) => nessionAttackFound |= a != null;

            await qe.Execute(null, onGlobalAttackFound, onAttackAssessedFound, null);

            Assert.AreEqual(expectGlobalAttack, globalAttackFound, "Global attack finding.");
            Assert.AreEqual(expectNessionAttack, nessionAttackFound, "Expected non-global attack.");
        }
        catch (Exception)
        {
            Console.WriteLine("Queries are:");
            foreach (IMessage q in t.Queries)
            {
                Console.WriteLine("  " + q.ToString());
            }

            Console.WriteLine("Initial states are:");
            foreach (State ini in t.InitialStates)
            {
                Console.WriteLine("  " + ini.ToString());
            }

            Console.WriteLine("Translated rules were as follows:");
            foreach (Rule r in t.Rules)
            {
                Console.WriteLine("  " + r.ToString());
            }
            throw;
        }
    }
}

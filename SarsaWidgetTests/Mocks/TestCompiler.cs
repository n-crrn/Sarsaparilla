using StatefulHorn;

namespace SarsaWidgetTests.Mocks;

public class TestCompiler : IClauseCompiler
{
    public static readonly int DelayMilliseconds = 500;

    private readonly List<string> GoodRules = new() {
        "know(x), know(y) -[ ]-> know(enc(x, y))",
        "(7) = know(sk) -[ ]-> know(pk(sk))",
        "(3) = new([bob_l], l_sl[]), new([bob_r], l_sr[]), know(m_f) -[ ]-> know(enc_a(<m_f, [bob_l], [bob_r]>, pk(sksd[])))"
    };

    public Task ConductSuccessfulRun() => ConductRun(GoodRules);

    private readonly List<string> BadRules = new() {
        "know(x), know(y) -[  know(enc(x, y))",
        "(7) = know(sk) -[ ]-> kow(pk(sk))",
        "(3) = new([bob_l], l_sl[]), new([bob_r], l_sr[]), know(m_f) -[ ]-> know(enc_a((m_f, [bob_l], [bob_r]), pk(sksd[])))"
    };

    public Task ConductFailureRun() => ConductRun(BadRules);

    public Task ConductFailureWithWarningRun() => ConductRun(BadRules, true);

    private async Task ConductRun(List<string> rules, bool warning = false)
    {
        OnReset?.Invoke(this);
        await Task.Delay(DelayMilliseconds);

        RuleParser parser = new();
        List<Rule> validRules = new();
        for (int i = 0; i < rules.Count; i++)
        {
            try
            {
                Rule result = parser.Parse(rules[i]);
                validRules.Add(result);
                OnRuleAddition?.Invoke(this, new(i + 1, rules[i], result, null));
            }
            catch (Exception ex)
            {
                OnRuleAddition?.Invoke(this, new(i + 1, rules[i], null, ex.Message));
            }
            await Task.Delay(DelayMilliseconds);
        }

        if (warning) {
            OnError?.Invoke(this, "One rule implies others.");
        }

        State initState = MessageParser.ParseState("test(init[])");
        IMessage queryMessage = MessageParser.ParseMessage("bob[]");
        OnComplete?.Invoke(this, new(new HashSet<State>() { initState }, queryMessage, null, validRules), null);
    }

    #region IClauseCompiler implementation.

    public event Action<IClauseCompiler>? OnReset;

    public event Action<IClauseCompiler, RuleAddedArgs>? OnRuleAddition;

    public event Action<IClauseCompiler, string>? OnError;

    public event Action<IClauseCompiler, QueryEngine?, string?>? OnComplete;

    #endregion
}

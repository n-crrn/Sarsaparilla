using StatefulHorn;

namespace SarsaWidgetTests.Mocks;

public class TestCompiler : IClauseCompiler
{
    public static readonly int DelayMilliseconds = 500;

    private readonly List<string> GoodRules = new() {
        "know(x), know(y) -[ ]-> know(enc(x, y))",
        "(7) = know(sk) -[ ]-> know(pk(sk))",
        "(3) = new([bob_l], l_sl[]), new([bob_r], l_sr[]), know(m_f) -[ ]-> know(enc_a((m_f, [bob_l], [bob_r]), pk(sksd[])))"
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

        for (int i = 0; i < rules.Count; i++)
        {
            OnRuleAddition?.Invoke(this, new(i + 1, rules[i]));
            await Task.Delay(DelayMilliseconds);
        }

        RuleParser parser = new();
        List<Rule> validRules = new();
        for (int i = 0; i < rules.Count; i++)
        {
            try
            {
                Rule result = parser.Parse(rules[i]);
                validRules.Add(result);
                OnRuleUpdate?.Invoke(this, new(i + 1, result, null));
            }
            catch (Exception ex)
            {
                OnRuleUpdate?.Invoke(this, new(i + 1, null, ex.Message));
            }
            await Task.Delay(DelayMilliseconds);
        }

        if (warning) {
            OnGeneralWarning?.Invoke(this, "One rule implies others.");
        }

        OnComplete?.Invoke(this, new Universe(validRules));
    }

    #region IClauseCompiler implementation.

    public event Action<IClauseCompiler>? OnReset;

    public event Action<IClauseCompiler, RuleAddedArgs>? OnRuleAddition;

    public event Action<IClauseCompiler, RuleUpdateArgs>? OnRuleUpdate;

    public event Action<IClauseCompiler, string>? OnGeneralWarning;

    public event Action<IClauseCompiler, Universe>? OnComplete;

    #endregion
}

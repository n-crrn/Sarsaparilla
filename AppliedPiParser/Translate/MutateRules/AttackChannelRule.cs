using System.Collections.Generic;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

/// <summary>
/// A Mutate Rule stating that a variable that reads from a socket can be set to an 
/// abitrary value if the channel's name is known.
/// </summary>
public class AttackChannelRule : MutateRule
{

    public AttackChannelRule(ReadSocket readSocket, string readVarName)
    {
        Socket = readSocket;
        VariableName = readVarName;
        Label = $"Attack:{VariableName}";
    }

    #region Properties

    public ReadSocket Socket { get; init; }

    public string VariableName { get; init; }

    #endregion

    public static IEnumerable<MutateRule> GenerateRulesForReceivePattern(
        ReadSocket socket,
        List<(string, string)> rxPattern,
        IfBranchConditions conditions,
        UserDefinition? userDef)
    {
        return from rx in rxPattern 
               select new AttackChannelRule(socket, rx.Item1) 
               { 
                   Conditions = conditions,
                   DefinedBy = userDef
               };
    }

    #region IMutateRule implementation.

    public override Rule GenerateRule(RuleFactory factory)
    {
        factory.RegisterPremise(Event.Know(new NameMessage(Socket.ChannelName)));
        factory.RegisterPremise(Event.Know(new VariableMessage(VariableName)));
        return GenerateStateConsistentRule(factory, ReadRule.VariableCellAsPremise(VariableName));
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Attack read rule to variable {VariableName}";

    public override bool Equals(object? obj)
    {
        return obj is AttackChannelRule acr && VariableName == acr.VariableName;
    }

    public override int GetHashCode() => VariableName.GetHashCode();

    #endregion

}

using System;
using System.Collections.Generic;

namespace StatefulHorn.Messages;

public class TupleVariableMessage : TupleMessage, IAssignableMessage
{

    public TupleVariableMessage(IEnumerable<IMessage> members) : base(members) { }

    public static TupleVariableMessage Ensure(TupleMessage tMsg)
    {
        if (tMsg is TupleVariableMessage tvm)
        {
            return tvm;
        }
        foreach (IMessage memberMsg in tMsg.Members)
        {
            if (memberMsg is not IAssignableMessage)
            {
                throw new ArgumentException($"Tuple {tMsg} contains non-variable members.");
            }
        }
        return new(tMsg.Members);
    }

    // FIXME: Reassess the requirement for this method.
    public static TupleVariableMessage AppendVariable(IAssignableMessage aMsg, VariableMessage vMsg)
    {
        List<IMessage> members = new();
        if (aMsg is TupleVariableMessage tvm)
        {
            members.AddRange(tvm.Members);
        }
        else if (aMsg is VariableMessage avMsg)
        {
            members.Add(avMsg);
        }
        else
        {
            throw new ArgumentException($"aMsg ({aMsg.GetType()}) is not a known assignable message.");
        }
        members.Add(vMsg);
        return new(members);
    }

}

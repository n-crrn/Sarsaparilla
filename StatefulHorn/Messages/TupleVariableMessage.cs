using System;
using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn.Messages;

/// <summary>
/// A tuple that is guaranteed to contain only variables. Required for some guard operations.
/// </summary>
public class TupleVariableMessage : TupleMessage, IAssignableMessage
{

    /// <summary>
    /// Create a new tuple variable message.
    /// </summary>
    /// <param name="members">Variables contained within the tuple.</param>
    public TupleVariableMessage(IEnumerable<IMessage> members) : base(members)
    {
        Ensure(this);
    }

    /// <summary>
    /// Create a new tuple variable message based on a sequence of variable names. This
    /// constructor is intended for use with unit tests.
    /// </summary>
    /// <param name="varNames">Sequence of variable names.</param>
    public TupleVariableMessage(params string[] varNames) 
        : base(from vn in varNames select new VariableMessage(vn))
    { }

    /// <summary>
    /// Ensures that the given TupleMessage can be treated as a TupleVariableMessage.
    /// </summary>
    /// <param name="tMsg">The message to check.</param>
    /// <returns>The provided message as a TupleVariableMessage.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if tMsg does not contain solely variables.
    /// </exception>
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

    /// <summary>
    /// If the provided TupleMessage can be treated as a TupleVariableMessage, then return it as
    /// such.
    /// </summary>
    /// <param name="tMsg">TupleMessage to assess.</param>
    /// <returns>The provided tMsg as a TupleVariableMessage if possible, null otherwise.</returns>
    public static TupleVariableMessage? TryEnsure(TupleMessage tMsg)
    {
        if (tMsg is TupleVariableMessage tvMsg)
        {
            return tvMsg;
        }
        foreach (IMessage memberMsg in tMsg.Members)
        {
            if (memberMsg is not IAssignableMessage)
            {
                return null;
            }
        }
        return new(tMsg.Members);
    }

}

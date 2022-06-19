using System;
using System.Collections.Generic;

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

}

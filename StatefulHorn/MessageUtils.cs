using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn.Messages;

namespace StatefulHorn;

public static class MessageUtils
{

    // Limits the total object creation and destruction within the library.
    private static readonly List<IMessage> VariableSubstitutions = new() { new VariableMessage("@_1") };

    private static void EnsureVariableCount(int count)
    {
        while (VariableSubstitutions.Count < count)
        {
            int id = VariableSubstitutions.Count + 1;
            VariableSubstitutions.Add(new VariableMessage($"@_{id}"));
        }
    }

    #region Message variable subscripting.

    public static IMessage SubscriptVariableMessage(IMessage originalMsg, string subscript)
    {
        VariableMessage originalVar = (VariableMessage)originalMsg;
        string originalName = originalVar.Name;
        string newName = originalName.Contains('_') ? $"{originalName}-{subscript}" : $"{originalName}_{subscript}";
        return new VariableMessage(newName);
    }

    #endregion
    #region Message blanking.

    public static IMessage BlankMessage(IMessage msg)
    {
        // Quick, easy, common test.
        if (msg is VariableMessage)
        {
            return VariableSubstitutions[0];
        }
        if (!msg.ContainsVariables)
        {
            return msg;
        }
        int varCounter = 0;
        return InnerBlankMessage(msg, new(), ref varCounter);
    }

    private static IMessage InnerBlankMessage(
        IMessage next, 
        Dictionary<VariableMessage, IMessage> foundCorrespondences, 
        ref int varCounter)
    {
        if (next is FunctionMessage fMsg)
        {
            List<IMessage> paramUpdated = new(fMsg.Parameters.Count);
            foreach (IMessage innerFMsg in fMsg.Parameters)
            {
                paramUpdated.Add(InnerBlankMessage(innerFMsg, foundCorrespondences, ref varCounter));
            }
            return new FunctionMessage(fMsg.Name, paramUpdated);
        }
        else if (next is TupleMessage tMsg)
        {
            List<IMessage> membersUpdated = new(tMsg.Members.Count);
            foreach (IMessage innerTMsg in tMsg.Members)
            {
                membersUpdated.Add(InnerBlankMessage(innerTMsg, foundCorrespondences, ref varCounter));
            }
            return new TupleMessage(membersUpdated);
        }
        else if (next is VariableMessage vMsg)
        {
            if (foundCorrespondences.TryGetValue(vMsg, out IMessage? foundMsg))
            {
                return foundMsg;
            }
            EnsureVariableCount(varCounter + 1);
            IMessage selectedSub = VariableSubstitutions[varCounter];
            foundCorrespondences[vMsg] = selectedSub;
            varCounter++;
            return selectedSub;
        }
        // Nothing to do if anything else is encountered.
        return next;
    }

    #endregion
    #region Message sorting.

    public static readonly Comparer SortComparer = new();

    public class Comparer : IComparer<IMessage>
    {
        public int Compare(IMessage? m1, IMessage? m2) => Sort(m1, m2);
    }

    /// <summary>
    /// Sorting method for messages. This method is not attached to the IMessage implementations
    /// as there is a need to have consistent sorting between different types of message.
    /// </summary>
    /// <param name="m1">First message to sort.</param>
    /// <param name="m2">Second message to sort.</param>
    /// <returns>Value in accordance with IComparable.CompareTo.</returns>
    public static int Sort(IMessage? m1, IMessage? m2)
    {
        if (m1 == null)
        {
            return 1;
        }
        if (m2 == null)
        {
            return -1;
        }

        bool m1VariableMsg = m1 is VariableMessage;
        bool m1NonceMsg = m1 is NonceMessage;
        bool m1NameMsg = m1 is NameMessage;
        bool m1FMsg = m1 is FunctionMessage;
        bool m2VariableMsg = m2 is VariableMessage;
        bool m2NonceMsg = m2 is NonceMessage;
        bool m2NameMsg = m2 is NameMessage;
        bool m2FMsg = m2 is FunctionMessage;

        if (m1VariableMsg)
        {
            if (m2VariableMsg)
            {
                return ((VariableMessage)m1).Name.CompareTo(((VariableMessage)m2).Name);
            }
            return 1;
        }
        if (m1NonceMsg)
        {
            if (m2NonceMsg)
            {
                return ((NonceMessage)m1).Name.CompareTo(((NonceMessage)m2).Name);
            }
            return m2VariableMsg ? -1 : 1;
        }
        if (m1NameMsg)
        {
            if (m2NameMsg)
            {
                return ((NameMessage)m1).Name.CompareTo(((NameMessage)m2).Name);
            }
            return (m2VariableMsg || m2NonceMsg) ? -1 : 1;
        }
        if (m1FMsg)
        {
            if (m2FMsg)
            {
                int fCmp = ((FunctionMessage)m1).Name.CompareTo(((FunctionMessage)m2).Name);
                if (fCmp == 0)
                {
                    return SortInnerList(((FunctionMessage)m1).Parameters, ((FunctionMessage)m2).Parameters);
                }
                return fCmp;
            }
            return (m2VariableMsg || m2NameMsg || m2NonceMsg) ? -1 : 1;
        }
        if (m2VariableMsg || m2NonceMsg || m2NameMsg || m2FMsg)
        {
            return -1;
        }

        TupleMessage t1Msg = (TupleMessage)m1;
        TupleMessage t2Msg = (TupleMessage)m2;
        return SortInnerList(t1Msg.Members, t2Msg.Members);
    }

    private static int SortInnerList(IReadOnlyList<IMessage> ml1, IReadOnlyList<IMessage> ml2)
    {
        for (int i = 0; i < Math.Min(ml1.Count, ml2.Count); i++)
        {
            int cmp = Sort(ml1[i], ml2[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }
        return ml1.Count.CompareTo(ml2.Count);
    }

    #endregion


}

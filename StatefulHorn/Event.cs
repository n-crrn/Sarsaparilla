using StatefulHorn.Messages;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn;

/// <summary>
/// An element that is either a premise of a rule or a result of a rule.
/// </summary>
public class Event : ISigmaUnifiable
{
    public enum Type
    {
        Know,   // Value is known.
        New,    // Nonce is generated at a given location.
        Make    // Create a new known token (should include nonces within sub-messages).
    }

    #region Constructors

    private Event(Type t, IMessage msg)
    {
        EventType = t;
        Message = msg;
        ContainsVariables = msg.ContainsVariables;
    }

    public static Event Know(IMessage msg) => new(Type.Know, msg);

    public static Event New(NonceMessage nonce) => new(Type.New, nonce);

    public static Event Make(IMessage msg) => new(Type.Make, msg);

    public Event PerformSubstitution(SigmaMap sigma) => new(EventType, Message.Substitute(sigma));

    #endregion

    public Type EventType { get; private init; }

    public readonly IMessage Message;

    public bool IsKnow => EventType == Type.Know;

    public bool IsNew => EventType == Type.New;

    public bool IsMake => EventType == Type.Make;

    #region Filtering

    public bool ContainsMessage(IMessage msg) => Message.ContainsMessage(msg);

    #endregion
    #region Unification determination

    public bool ContainsVariables { get; init; }

    private HashSet<IMessage>? _Variables;

    public IReadOnlySet<IMessage> Variables
    {
        get
        {
            if (_Variables == null)
            {
                _Variables = new();
                Message.CollectVariables(_Variables);
            }
            return _Variables;
        }
    }

    public bool CanBeUnifiedTo(ISigmaUnifiable other, Guard g, SigmaFactory subs)
    {
        return other is Event ev 
            && IsKnow 
            && ev.IsKnow 
            && Message.DetermineUnifiedToSubstitution(ev.Message, g, subs);
    }

    public bool CanBeUnifiableWith(ISigmaUnifiable other, Guard fwdG, Guard bwdG, SigmaFactory subs)
    {
        return other is Event ev
            && IsKnow
            && ev.IsKnow
            && Message.DetermineUnifiableSubstitution(ev.Message, fwdG, bwdG, subs);
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is Event ev && EventType == ev.EventType && Message.Equals(ev.Message);
    }

    public override int GetHashCode() => Message.GetHashCode();

    public static bool operator ==(Event? ev1, Event? ev2) => Equals(ev1, ev2);

    public static bool operator !=(Event? ev1, Event? ev2) => !Equals(ev1, ev2);

    private string? Description;

    public override string ToString()
    {
        if (Description == null)
        {
            Description = EventType switch
            {
                Type.Know => $"know({FirstMessageAsString})",
                Type.New => $"new({FirstMessageAsString})",
                Type.Make => $"make({FirstMessageAsString})",
                _ => throw new System.NotImplementedException()
            };
        }
        return Description;
    }

    private string FirstMessageAsString => Message.ToString();

    #endregion
}

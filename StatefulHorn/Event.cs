using StatefulHorn.Messages;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn;

public class Event : ISigmaUnifiable
{
    public enum Type
    {
        Know,   // Value is known.
        New,    // Nonce is generated at a given location.
        Init,   // Protocol start.
        Accept, // Protocol successful authentication end
        Leak,   // Protocol secrecy failure with leaked message.
        Make    // Create a new known token (should include nonces within sub-messages).
    }

    #region Constructors

    private Event(Type t, IEnumerable<IMessage> messages)
    {
        EventType = t;
        _Messages = new(messages);
        // There must be at least one message.
        Debug.Assert(_Messages.Count > 0);

        foreach (IMessage msg in Messages)
        {
            if (msg.ContainsVariables)
            {
                ContainsVariables = true;
                break;
            }
        }
    }

    public static Event Know(IMessage msg) => new(Type.Know, new IMessage[] { msg });

    public static Event New(NonceMessage nonce, NameMessage loc)
    {
        Event ev = new(Type.New, new IMessage[] { nonce });
        ev.LocationId = loc;
        return ev;
    }

    public static Event New(NonceMessage nonce) => New(nonce, new NameMessage("l"));
    // FIXME: Ensure the location part is removed.

    public static Event Init(IEnumerable<IMessage> knownMessages) => new(Type.Init, knownMessages);

    public static Event Init(IMessage knownMsg) => new(Type.Init, new List<IMessage>() { knownMsg });

    public static Event Accept(IEnumerable<IMessage> acceptMessages) => new(Type.Accept, acceptMessages);

    public static Event Accept(IMessage accMsg) => new(Type.Accept, new List<IMessage>() { accMsg });

    public static Event Leak(IMessage msg) => new(Type.Leak, new IMessage[] { msg });

    public static Event Make(IMessage msg) => new(Type.Make, new IMessage[] { msg });

    public Event PerformSubstitution(SigmaMap sigma)
    {
        Event ev = new(EventType, from m in _Messages select m.PerformSubstitution(sigma));
        if (ev.EventType == Type.New)
        {
            ev.LocationId = LocationId;
        }
        return ev;
    }

    #endregion

    public Type EventType { get; init; }

    private readonly List<IMessage> _Messages;

    public IReadOnlyList<IMessage> Messages => _Messages;

    public NameMessage? LocationId { get; private set; }

    public bool IsAcceptOrLeak => EventType == Type.Leak || EventType == Type.Accept;

    #region Filtering

    public bool ContainsMessage(IMessage msg)
    {
        foreach (IMessage m in _Messages)
        {
            if (m.ContainsMessage(msg))
            {
                return true;
            }
        }
        return false;
    }

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
                foreach(IMessage msg in Messages)
                {
                    msg.CollectVariables(_Variables);
                }
            }
            return _Variables;
        }
    }

    public bool IsKnow => EventType == Type.Know;

    public bool IsNew => EventType == Type.New;

    public bool CanBeUnifiedTo(ISigmaUnifiable other, Guard g, SigmaFactory subs)
    {
        return other is Event ev && IsKnow && ev.IsKnow && subs.CanUnifyMessagesOneWay(_Messages, ev._Messages, g);
    }

    public bool CanBeUnifiableWith(ISigmaUnifiable other, Guard g, SigmaFactory subs)
    {
        return other is Event ev && IsKnow && ev.IsKnow && subs.CanUnifyMessagesBothWays(_Messages, ev._Messages, g);
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        if (obj is Event ev && EventType == ev.EventType)
        {
            if (EventType == Event.Type.Init || EventType == Event.Type.Accept)
            {
                return _Messages.Count == ev._Messages.Count && _Messages.SequenceEqual(ev._Messages);
            }
            return _Messages[0].Equals(ev._Messages[0]); //&& NameMessage.Equals(LocationId, ev.LocationId);
        }
        return false;
    }

    public override int GetHashCode() => _Messages[0].GetHashCode();

    public static bool operator ==(Event? ev1, Event? ev2) => Equals(ev1, ev2);

    public static bool operator !=(Event? ev1, Event? ev2) => !Equals(ev1, ev2);

    private string? Description;

    public override string ToString()
    {
        if (Description == null)
        {
            Description = EventType switch
            {
                Type.Accept => $"accept({MessagesAsString})",
                Type.Init => $"init({MessagesAsString})",
                Type.Know => $"know({FirstMessageAsString})",
                Type.Leak => $"leak({FirstMessageAsString})",
                Type.New => $"new({FirstMessageAsString}, {LocationId!})",
                Type.Make => $"make({FirstMessageAsString})",
                _ => throw new System.NotImplementedException()
            };
        }
        return Description;
    }

    private string FirstMessageAsString => _Messages[0].ToString();

    private string MessagesAsString => string.Join(", ", _Messages);

    #endregion
}

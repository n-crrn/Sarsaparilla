using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatefulHorn;

/// <summary>
/// Checks two snapshots (including their history) to see if they are unifiable.
/// </summary>
public class Trace
{

    public Trace(Snapshot ss)
    {
        Header = ss;
        if (ss.HasPredecessor)
        {
            (Snapshot prior, Snapshot.Ordering priorOrd) = ss.PriorSnapshots[0];
            Priors = TreeToStack(prior, priorOrd);
        }
        else
        {
            Priors = new();
        }
    }

    public Snapshot Header;

    public Stack<(Snapshot, Snapshot.Ordering)> Priors;

    public List<(Snapshot, int)>? IsUnifiableWith(Trace other, Guard g, SigmaFactory sf)
    {
        if (Header.Condition.Name == other.Header.Condition.Name &&
            Header.Condition.CanBeUnifiableWith(other.Header.Condition, g, sf))
        {
            Stack<(Snapshot, Snapshot.Ordering)> dispPriors = new(Priors);
            Stack<(Snapshot, Snapshot.Ordering)> otherPriors = new(other.Priors);
            return TracesUnifiable(dispPriors, otherPriors, 1, g, sf);
        }
        return null;
    }

    private static Stack<(Snapshot, Snapshot.Ordering)> TreeToStack(Snapshot ss, Snapshot.Ordering ord)
    {
        Stack<(Snapshot, Snapshot.Ordering)> soFar;
        if (ss.HasPredecessor)
        {
            (Snapshot prior, Snapshot.Ordering priorOrd) = ss.PriorSnapshots[0];
            soFar = TreeToStack(prior, priorOrd);
        }
        else
        {
            soFar = new();
        }
        soFar.Push((ss, ord));
        return soFar;
    }

    private static List<(Snapshot, int)>? TracesUnifiable(
        Stack<(Snapshot, Snapshot.Ordering)> guide,
        Stack<(Snapshot, Snapshot.Ordering)> other,
        int offset,
        Guard g,
        SigmaFactory sf)
    {
        if (guide.Count == 0)
        {
            return new();
        }

        (Snapshot currentGuideSS, Snapshot.Ordering currentGuideOrd) = guide.Pop();

        while (other.Count > 0)
        {
            (Snapshot nextOtherSS, Snapshot.Ordering nextOtherOrd) = other.Pop();
            if (nextOtherOrd == Snapshot.Ordering.Unchanged ||
                (currentGuideOrd == Snapshot.Ordering.ModifiedOnceAfter && nextOtherOrd == Snapshot.Ordering.LaterThan))
            {
                return null;
            }
            if (currentGuideSS.Condition.CanBeUnifiableWith(nextOtherSS.Condition, g, sf))
            {
                List<(Snapshot, int)>? corres = TracesUnifiable(guide, other, offset + 1, g, sf);
                if (corres != null)
                {
                    corres.Add((currentGuideSS, offset));
                }
                return corres;
            }
            if (currentGuideOrd != Snapshot.Ordering.LaterThan)
            {
                return null; // Already invalid.
            }
        }
        // We have run out of candidates without matching currentGuideSS.
        return null;
    }

}

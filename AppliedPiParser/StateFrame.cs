using System;
using System.Collections.Generic;
using System.Linq;

using StatefulHorn;

namespace AppliedPi;

/// <summary>
/// Instances of this class are used as part of the translation from Applied Pi Calculus to 
/// StatefulHorn clauses.
/// </summary>
public class StateFrame
{

    public StateFrame(State cell)
    {
        _Cells.Add(cell);
    }

    public StateFrame(IEnumerable<State> cells)
    {
        _Cells.AddRange(cells);
        _Cells.Sort();
    }

    private readonly List<State> _Cells = new();

    public IEnumerable<State> Cells => _Cells;

    public StateFrame Update(State newState)
    {
        // Don't try any fancy algorithm to track down the cell - there will 
        // rarely be more than one in the first place.
        for (int i = 0; i < _Cells.Count; i++)
        {
            if (_Cells[i].Name == newState.Name)
            {
                _Cells[i] = newState;
                return this;
            }
        }
        _Cells.Add(newState);
        return this;
    }

    public StateFrame Clone()
    {
        return new(_Cells);
    }

    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        if (obj is not StateFrame sf)
        {
            return false;
        }
        return _Cells.Count == sf._Cells.Count && _Cells.SequenceEqual(sf._Cells);
    }

    public override int GetHashCode()
    {
        int hc = 7883;
        foreach (State c in _Cells)
        {
            hc = hc * 7901 + c.GetHashCode();
        }
        return hc;
    }

    public override string ToString() => string.Join(", ", _Cells);

    #endregion

}

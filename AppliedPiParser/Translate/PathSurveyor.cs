using System.Collections.Generic;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate;

/// <summary>
/// This class is used to determine the "path" (sequence) of correct finite read and finite
/// write operations for certain operations to be locked against.
/// </summary>
public class PathSurveyor
{

    /// <summary>
    /// Construct a new PathSurveyor for a model's branch of control.
    /// </summary>
    /// <param name="prevSockets">
    /// Finite sockets that have been previously used (and are now shut).
    /// </param>
    /// <param name="thisBranchSockets">Finite sockets in this branch.</param>
    public PathSurveyor(IEnumerable<Socket> prevSockets, IEnumerable<Socket> thisBranchSockets)
    {
        PreviousSockets = new List<Socket>(prevSockets);
        foreach (Socket s in thisBranchSockets)
        {
            InteractionCount[s] = 0;
        }
    }

    #region Properties.

    /// <summary>A list of all the sockets that were previously shut.</summary>
    public IReadOnlyList<Socket> PreviousSockets { get; private init; }

    /// <summary>
    /// A dictionary of all sockets and how many times they have been interacted (that is, read
    /// from or written to) during the branch of control.
    /// </summary>
    private readonly Dictionary<Socket, int> InteractionCount = new();

    /// <summary>
    /// Indicates that finites sockets are being tracked by this Surveyor.
    /// </summary>
    public bool HasInteractions => InteractionCount.Count > 0;

    #endregion
    #region Interaction tracking and socket retrieval.

    /// <summary>
    /// Note an interaction on the given socket. The socket MUST be known by this PathSurveyor or
    /// an exception will be raised.
    /// </summary>
    /// <param name="s">Socket that was read from or written to.</param>
    public void AddInteractionFor(Socket s)
    {
        InteractionCount[s] = InteractionCount[s] + 1;
    }

    /// <summary>
    /// All sockets dealt with by this PathSurveyor. This method can be used to determine which
    /// sockets need to be shut at the end of a branch of control.
    /// </summary>
    /// <returns>
    /// List of all sockets (both previous and current sockets) that are known by this 
    /// PathSurveyor.
    /// </returns>
    public IList<Socket> AllSockets()
    {
        List<Socket> allS = new();
        allS.AddRange(PreviousSockets);
        allS.AddRange(InteractionCount.Keys);
        return allS;
    }

    /// <summary>
    /// Returns the finite sockets currently in use.
    /// </summary>
    /// <returns>List of finite sockets.</returns>
    public IList<Socket> InteractionSockets()
    {
        return new List<Socket>(InteractionCount.Keys);
    }

    /// <summary>
    /// Returns whether the given socket has been interacted with during this branch of control.
    /// An exception will be thrown if the socket is not an interactive socket within this 
    /// instance.
    /// </summary>
    /// <param name="s">Socket to check.</param>
    /// <returns>True if the socket has been interacted with (i.e.read or written to).</returns>
    public bool HasInteractedWith(Socket s) => InteractionCount[s] > 0;

    #endregion
    #region Marker and marker creation.

    /// <summary>
    /// Instances of this class are used to "mark" an event along the "path" of the branch of
    /// control. A Marker object allows you to set the states within a Stateful Horn Clause
    /// easily that have arisen along the path.
    /// </summary>
    public class Marker
    {
        /// <summary>
        /// Creates a new Marker. This constructor typically only be used as part of unit testing.
        /// Instead use PathSurveyor.MarkPath().
        /// </summary>
        /// <param name="socketStates">
        /// List of sockets and their corresponding historical states. Any snapshot generated and
        /// returned to outside entities will be based on the last state in each sockets' list.
        /// </param>
        /// <seealso cref="PathSurveyor.MarkPath"/>
        public Marker(IList<(Socket, IList<State>)> socketStates)
        {
            SocketStates = socketStates;
        }

        /// <summary>Sockets and their historical states.</summary>
        private readonly IList<(Socket, IList<State>)> SocketStates;

        /// <summary>
        /// Register states held within the marker with the given Rule Factory.
        /// </summary>
        /// <param name="factory">
        /// Rule Factory to register state with using RuleFactory.RegisterState(...).
        /// </param>
        /// <returns>
        /// A dictionary that associates sockets with their final registered snapshot.
        /// </returns>
        /// <seealso cref="RuleFactory.RegisterState"/>
        public IDictionary<Socket, Snapshot> Register(RuleFactory factory)
        {
            Dictionary<Socket, Snapshot> snapshots = new();
            foreach ((Socket sock, IList<State> stat) in SocketStates)
            {
                Snapshot ss = factory.RegisterState(stat[0]);
                for (int i = 1; i < stat.Count; i++)
                {
                    Snapshot tmpSS = factory.RegisterState(stat[i]);
                    tmpSS.SetModifiedOnceLaterThan(ss);
                    ss = tmpSS;
                }
                snapshots[sock] = ss;
            }
            return snapshots;
        }

        /// <summary>
        /// Register states held within the marker with the given Rule Factory, and return
        /// the last snapshot of the requested socket.
        /// </summary>
        /// <param name="factory">
        /// Rule Factory to register state with using RuleFactory.RegisterState(...).
        /// </param>
        /// <param name="socket">
        /// Which socket to return the corresponding final snapshot of.
        /// </param>
        /// <returns>Final snapshot of requested socket.</returns>
        /// <seealso cref="Marker.Register"/>
        /// <seealso cref="RuleFactory.RegisterState(State)"/>
        public Snapshot RegisterAndRetrieve(RuleFactory factory, Socket socket)
        {
            return Register(factory)[socket];
        }

        public override bool Equals(object? other)
        {
            return other is Marker m && SocketStates.ToHashSet().SetEquals(m.SocketStates);
        }

        public override int GetHashCode() => SocketStates.Count;
    }

    /// <summary>
    /// Returns a Marker object representing this point along the main path of the PathSurveyor's
    /// branch of control.
    /// </summary>
    /// <returns>A Marker that can be used to set the states within a Stateful Horn Rule.</returns>
    public Marker MarkPath()
    {
        List<(Socket, IList<State>)> socketStates = new();

        foreach (Socket ps in PreviousSockets)
        {
            socketStates.Add((ps, new List<State>() { ps.ShutState() }));
        }

        foreach ((Socket branchS, int useCount) in InteractionCount)
        {
            if (branchS.Direction == SocketDirection.In) // Read socket.
            {
                List<State> readSeq = new() { branchS.InitialState() };
                if (useCount == 0)
                {
                    readSeq.Add(branchS.WaitingState());
                }
                else
                {
                    for (int i = 0; i < useCount; i++)
                    {
                        readSeq.Add(branchS.WaitingState());
                        VariableMessage rMsg = new($"@{branchS}@v{i}");
                        readSeq.Add(branchS.ReadState(rMsg));
                    }
                }
                socketStates.Add((branchS, readSeq));
            }
            else // Write socket.
            {
                List<State> writeSeq = new() { branchS.InitialState() };
                for (int i = 0; i < useCount; i++)
                {
                    writeSeq.Add(branchS.WaitingState());
                    VariableMessage wMsg = new($"@{branchS}@v{i}");
                    writeSeq.Add(branchS.WriteState(wMsg));
                }
                writeSeq.Add(branchS.WaitingState());
                socketStates.Add((branchS, writeSeq));
            }
        }

        return new Marker(socketStates/*, InteractionCount*/);
    }

    #endregion

}

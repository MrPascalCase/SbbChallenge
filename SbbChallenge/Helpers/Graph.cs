using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using sbbChallange.Layers;
using static sbbChallange.IntegrityChecks.Asserts;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch GraphChecks = new Switch(nameof(SbbChallenge.Helpers.Graph) + "." + nameof(GraphChecks), 
            "add/remove: arc (not) exists, arcExists: forward/backward eq.");
    }
}

namespace SbbChallenge.Helpers
{
    public struct Arc : IComparable<Arc>, IEquatable<Arc>
    {
        public readonly int Tail;
        public readonly int Head;
        public readonly int MachineId;       // see thesis, 3.8, Parallel machines
        public readonly TimeSpan Length;

        public static Arc BetweenOccupations(MachineOccupation fst, MachineOccupation snd)
        {
            Assert(GraphChecks, fst.MachineId == snd.MachineId);
            return new Arc(fst.LastOperation + 1, snd.FirstOperation, fst.ReleaseTime, fst.MachineId);
        }

        public Arc(int tail, int head, TimeSpan length, int machineId = -1)
        {
            Head = head;
            Length = length;
            Tail = tail;
            MachineId = machineId;
        }

        public bool Equals(Arc other)
        {
            return Head == other.Head && Tail == other.Tail && MachineId == other.MachineId &&
                   Length.Equals(other.Length);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is Arc other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Head;
                hashCode = (hashCode * 397) ^ Tail;
                hashCode = (hashCode * 397) ^ MachineId;
                return hashCode;
            }
        }

        public override string ToString() => $"({Tail}->{Head}" + (MachineId == -1 ? ")" : $"|{MachineId})");

        //private int Code => Head + 3000 * Tail + 3000 * 3000 * MachineId;

        public int CompareTo(Arc other)
        {
            //return other.Code - Code;
            return GetHashCode() - other.GetHashCode();
        }
    }

    /// <summary>
    /// Implements IGraph, see IGraph for documentation
    /// </summary>
    public class Graph : IGraph
    {
        /*    Implementation idea:
         *      * We use 2 arrays (_forward, _backward) of adjacency lists as the basic structure.
         * 
         *      * Forward and backward arcs are stored in the resp. arrays. Example:
         *            arc = { Tail = t, Head = h }
         *            would be stored as
         *                 _forward[t].add(h);
         *                 _backward[h].add(t);
         * 
         *      * The forward/backward arrays do not need the information of the tail/head resp.
         *         ==> internal Arc data structure without them is used
         *
         *      * As we saw that the degree of vertices in the sbb dataset is often 1 .. 3, we allocate 3 arcs for
         *         each adjacency list, then use a list for additional arcs.
         *         (keep the memory allocation compact, for fast graph traversal)
         * 
         *      * The label of an arc is just its length (a Timespan) and it's machine Id. As there are no parallel
         *         arcs of the same machine Id, we do not use the timespan for arc comparison, and as a safety check
         *         do not allow to store parallel arcs of the same machine Id.
         */
        
        struct InternalArc : IEquatable<InternalArc>, IComparable<InternalArc>
        {
            public readonly int OtherEnd;
            public readonly int MachineId;
            public readonly TimeSpan Length;

            public InternalArc(int otherEnd, int machineId, TimeSpan length)
            {
                OtherEnd = otherEnd;
                MachineId = machineId;
                Length = length;
            }

            public bool Equals(InternalArc other)
            {
                return OtherEnd == other.OtherEnd && MachineId == other.MachineId;
            }

            public override bool Equals(object obj)
            {
                return obj is InternalArc other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (OtherEnd * 397) ^ MachineId;
                }
            }

            public int CompareTo(InternalArc other)
            {
                var otherEndComparison = OtherEnd.CompareTo(other.OtherEnd);
                if (otherEndComparison != 0) return otherEndComparison;
                //var resourceIdComparison = ResourceId.CompareTo(other.ResourceId);
                //if (resourceIdComparison != 0) return resourceIdComparison;
                //return Length.CompareTo(other.Length);
                return MachineId.CompareTo(other.MachineId);
            }

            public override string ToString()
            {
                return $"to = {OtherEnd}, machine = {MachineId}, length = {Length.Show()}";
            }
        }

        /// <summary>
        /// In the SBB problem instances it is very common for vertices to have in/out-degree 2 or 3. Hence this
        /// implementation allocates 3 arcs within a struct. Hence most of the graph should be saved in one block of
        /// continuous memory to allow fast traversal.
        /// </summary>
        private struct AdjList : IEnumerable<InternalArc>
        {
            // An immutable data structure.
            private readonly InternalArc _arc1;
            private readonly InternalArc _arc2;
            private readonly InternalArc _arc3;
            private readonly ImmutableList<InternalArc> _next;

            public IEnumerator<InternalArc> GetEnumerator()
            {
                if (_arc1.Equals(default)) yield break;
                yield return _arc1;
                if (_arc2.Equals(default)) yield break;
                yield return _arc2;
                if (_arc3.Equals(default)) yield break;
                yield return _arc3;
                if (ReferenceEquals(null, _next) || _next.Count == 0) yield break;
                foreach (InternalArc arc in _next) yield return arc;
            }

            private AdjList(InternalArc arc1, InternalArc arc2, InternalArc arc3, ImmutableList<InternalArc> next)
            {
                _arc1 = arc1;
                _arc2 = arc2;
                _arc3 = arc3;
                _next = next;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            [Pure]
            public AdjList AddArc(InternalArc arc)
            {
                if (arc.Equals(default))
                    throw new ArgumentException(
                        "Cannot store a arc==default, we could not tell it apart form empty memory.");

                if (_arc1.Equals(default)) return new AdjList(arc, default, default, null);
                if (_arc2.Equals(default)) return new AdjList(_arc1, arc, default, null);
                if (_arc3.Equals(default)) return new AdjList(_arc1, _arc2, arc, null);
                if (_next == null) return new AdjList(_arc1, _arc2, _arc3, ImmutableList<InternalArc>.Empty.Add(arc));
                return new AdjList(_arc1, _arc2, _arc3, _next.Add(arc));
            }

            [Pure]
            public AdjList RemoveArc(InternalArc arc)
            {
                if (arc.Equals(default))
                    throw new ArgumentException(
                        "Cannot store a arc==default, we could not tell it apart form empty memory.");

                if (!this.Contains(arc))
                {
                    Console.WriteLine($"Searching for arc {arc}");

                    Console.WriteLine("We have the arcs:");
                    foreach (InternalArc internalArc in this)
                    {
                        Console.WriteLine($"\t{internalArc}");
                    }

                    throw new ArgumentException();
                }

                if (_next == null || _next.Count == 0)
                {
                    if (_arc1.Equals(arc)) return new AdjList(_arc2, _arc3, default, null);
                    if (_arc2.Equals(arc)) return new AdjList(_arc1, _arc3, default, null);
                    if (_arc3.Equals(arc)) return new AdjList(_arc1, _arc2, default, null);

                    throw new Exception();
                }

                if (_next.Contains(arc))
                {
                    return new AdjList(_arc1, _arc2, _arc3, _next.Remove(arc));
                }

                var lastArc = _next.First();
                var newNext = _next.Count == 1 ? null : _next.Remove(lastArc);

                if (_arc1.Equals(arc)) return new AdjList(_arc2, _arc3, lastArc, newNext);
                if (_arc2.Equals(arc)) return new AdjList(_arc1, _arc3, lastArc, newNext);
                if (_arc3.Equals(arc)) return new AdjList(_arc1, _arc2, lastArc, newNext);

                throw new Exception();
            }
        }

        private readonly AdjList[] _forward;
        private readonly AdjList[] _backward;

        public Graph(int count)
        {
            _forward = new AdjList[count];
            _backward = new AdjList[count];
        }

        private Graph(AdjList[] forward, AdjList[] backward)
        {
            _forward = forward;
            _backward = backward;
        }

        public void AddArc(int tail, int head, int machineId, TimeSpan length)
        {
            Assert(
                GraphChecks,
                !ArcExists(tail, head, machineId));

            _forward[tail] = _forward[tail].AddArc(new InternalArc(head, machineId, length));
            _backward[head] = _backward[head].AddArc(new InternalArc(tail, machineId, length));
        }

        public void RemoveArc(int tail, int head, int machineId)
        {
            Assert(
                GraphChecks,
                ArcExists(tail, head, machineId));

            _forward[tail] = _forward[tail].RemoveArc(new InternalArc(head, machineId, TimeSpan.Zero));
            _backward[head] = _backward[head].RemoveArc(new InternalArc(tail, machineId, TimeSpan.Zero));
        }

        [Pure]
        public IEnumerable<Arc> OutgoingArcs(int vertex) =>
            _forward[vertex].Select(ia => new Arc(vertex, ia.OtherEnd, ia.Length, ia.MachineId));

        [Pure]
        public IEnumerable<Arc> IncomingArcs(int vertex) =>
            _backward[vertex].Select(ia => new Arc(ia.OtherEnd, vertex, ia.Length, ia.MachineId));

        public int Count => _forward.Length;

        [Pure]
        private bool ArcExists(int tail, int head, int machineId)
        {
            bool result = _forward[tail].Contains(new InternalArc(head, machineId, TimeSpan.Zero));
            Assert(
                GraphChecks,
                result == _backward[head].Contains(new InternalArc(tail, machineId, TimeSpan.Zero)));
            return result;
        }

        [Pure]
        public bool ArcExists(int tail, int head, int machineId, out TimeSpan length)
        {
            if (!ArcExists(tail, head, machineId))
            {
                length = TimeSpan.Zero;
                return false;
            }

            length = _forward[tail].First(ia => ia.OtherEnd == head && ia.MachineId == machineId).Length;
            return true;
        }


        public bool Equals(IGraph other)
            => this.IsEquivalentTo(other);

        public override bool Equals(object other)
            => this.IsEquivalentTo((IGraph) other);

        public override int GetHashCode()
        {
            throw new Exception("hashcodes of the graph are not intended to be used.");
        }

        public IGraph Clone()
        {
            var forwardCopy = new AdjList[Count];
            Array.Copy(_forward, forwardCopy, Count);

            var backwardCopy = new AdjList[Count];
            Array.Copy(_backward, backwardCopy, Count);

            return new Graph(forwardCopy, backwardCopy);
        }

        [Pure]
        public static bool PathExists(IGraph graph, int source, int target)
        {
            var marked = new bool[graph.Count];
            var stack = new Stack<int>();
            stack.Push(source);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == target)
                {
                    return true;
                }

                foreach (int h in graph.OutgoingArcs(current)
                    .Select(a => a.Head)
                    .Where(h => !marked[h]))
                {
                    marked[h] = true;
                    stack.Push(h);
                }
            }

            return false;
        }
    }
}
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using sbbChallange.Layers;
using sbbChallange.ProblemDefinition;
using static sbbChallange.IntegrityChecks.Asserts;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch JobSequenceChecks = 
            new Switch(nameof(SbbChallenge.Helpers.JobSequence) + "." + nameof(JobSequenceChecks), 
                "argument occupations exist",
                "machine ids on removal");

        public static readonly Switch JobSequenceClassInvariant = 
                new Switch(nameof(SbbChallenge.Helpers.JobSequence) + "." + nameof(JobSequenceClassInvariant), 
                "Correct count", 
                "Matching indices", 
                "Matching (same) machine id",
                "Correct pointers and nulls");
    }

}

namespace SbbChallenge.Helpers
{
    public class JobSequence : 
        IJobSequence
    {
        class PreviousNext : IEquatable<PreviousNext>
        {
            public MachineOccupation Prev;
            public MachineOccupation Next;
            public int Index;

            public PreviousNext(MachineOccupation prev, MachineOccupation next, int index)
            {
                Prev = prev;
                Next = next;
                Index = index;
            }

            // Equality members need to be present since the ImmutableDictionary checks if an element to be overwritten
            // (setItem) is different from the one present.
            public bool Equals(PreviousNext other)
            {
                // ReSharper disable once PossibleNullReferenceException
                return Equals(Prev, other.Prev) && Equals(Next, other.Next) && Index == other.Index;
            }

            public override bool Equals(object obj)
            {
                return obj is PreviousNext other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    // ReSharper disable NonReadonlyMemberInGetHashCode
                    var hashCode = (Prev != null ? Prev.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (Next != null ? Next.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ Index;
                    return hashCode;
                }
            }

            public PreviousNext Clone()
            {
                return new PreviousNext(Prev, Next, Index);
            }

            public override string ToString()
            {
                return $"{{ Prev={Prev}, Next={Next}, Index={Index} }}";
            }
        }

        

        [System.Diagnostics.Conditional("DEBUG")]
        private void AssertClassInvariant()
        {
            if (!JobSequenceClassInvariant.On) return;

            // ReSharper disable once RedundantCast (more readable)
            Assert(JobSequenceClassInvariant, Count == ((IEnumerable<MachineOccupation>) this).Count(),
                $"{nameof(JobSequence)}: traversal must have the same count as the map,  _map.Count == this.Count()");

            (MachineOccupation, PreviousNext)[] asArray = this.Select(m => (m, _map[m])).ToArray();

            Assert(JobSequenceClassInvariant, asArray.Numbered().All(t => t.Item1.Item2.Index == t.Item2),
                "Cached indices must match with the resp. position: asArray.Numbered().All(t => t.Item1.Item2.Index == t.Item2)");

            Assert(JobSequenceClassInvariant, asArray.All(x => x.Item1.MachineId == _machine),
                "asArray.All(x => x.Item1.MachineId == _machine)");

            if (Count > 0)
            {
                Assert(JobSequenceClassInvariant, asArray[0].Item1.Equals(_first),
                    "asArray[0].Item1.Equals(_first)");

                Assert(JobSequenceClassInvariant, asArray.Last().Item1.Equals(_last),
                    "asArray.Last().Item1.Equals(_last)");

                Assert(JobSequenceClassInvariant, asArray[0].Item2.Prev == null,
                    "asArray[0].Item2.Prev == null");

                Assert(JobSequenceClassInvariant, asArray.Last().Item2.Next == null,
                    "asArray.Last().Item2.Next == null");
            }

            Assert(JobSequenceClassInvariant, asArray.Skip(1).All(x => x.Item2.Prev != null),
                "asArray.Skip(1).All(x => x.Item2.Prev != null)");

            Assert(JobSequenceClassInvariant, asArray.Reverse().Skip(1).All(x => x.Item2.Next != null),
                "asArray.Reverse().Skip(1).All(x => x.Item2.Next != null)");

            Assert(JobSequenceClassInvariant,
                asArray.Pairwise((x, y) => x.Item2.Next.Equals(y.Item1) && y.Item2.Prev.Equals(x.Item1)).All(x => x));

            if (_machine >= Problem.MachineCount)
            {
                // Corresponds to a connection.
                Assert(JobSequenceClassInvariant, 0 <= Count && Count <= 2);
                Assert(JobSequenceClassInvariant, this.Count(mOcc => mOcc.Type == MachineOccupationType.ConnectionOrigin) <= 1);
                Assert(JobSequenceClassInvariant, this.Count(mOcc => mOcc.Type == MachineOccupationType.ConnectionTarget) <= 1);
                Assert(JobSequenceClassInvariant, this.Count(mOcc => mOcc.Type == MachineOccupationType.Normal) == 0);
                
            }
            else
            {
                // Corresponds to an actual machine.
                Assert(JobSequenceClassInvariant, this.All(mOcc => mOcc.Type == MachineOccupationType.Normal));
            }
        }

        private Dictionary<MachineOccupation, PreviousNext> _map;
        private readonly int _machine;
        private readonly IGraphLayer _graphLayer;
        private MachineOccupation _first;
        private MachineOccupation _last;

        private ISolution Solution => _graphLayer.CostLayer.Solution;
        private Problem Problem => Solution.Problem;
        private bool CorrespondsToConnection =>
            Problem.MachineCount <= _machine && _machine < Problem.MachineAndConnectionCount;

        public int Count => _map.Count;

        public JobSequence(int machineId, IGraphLayer graphLayer)
        {
            _map = new Dictionary<MachineOccupation, PreviousNext>();
            _machine = machineId;
            _graphLayer = graphLayer;
            _first = null;
            _last = null;
        }

        private JobSequence(
            Dictionary<MachineOccupation, PreviousNext> map,
            int machine, MachineOccupation first, MachineOccupation last, IGraphLayer graphLayer)
        {
            _map = map;
            _machine = machine;
            _first = first;
            _last = last;
            _graphLayer = graphLayer;
        }

        private Arc Connect(MachineOccupation first, MachineOccupation second)
        {
            if (first.Equals(default) || second.Equals(default))
            {
                throw new Exception();
            }

            int tail = first.LastOperation + 1;
            int head = second.FirstOperation;

            TimeSpan releaseTime = _graphLayer.CostLayer.Solution.GetMachineReleaseTime(first, second);

            _graphLayer.AddArc(tail, head, first.MachineId, releaseTime);
            return new Arc(tail, head, releaseTime, first.MachineId);
        }

        private void Disconnect(MachineOccupation first, MachineOccupation second)
        {
            int tail = first.LastOperation + 1;
            int head = second.FirstOperation;
            _graphLayer.RemoveArc(tail, head, first.MachineId);
        }

        /// <summary>
        /// Changes the indices of the data associated with 'occupation' and all later ones.
        /// Index += delta. 
        /// </summary>
        private void ChangeLaterIndices(MachineOccupation occupation, int delta)
        {
            if (occupation == null) return;

            if (_map.TryGetValue(occupation, out var data))
            {
                data.Index += delta;
                ChangeLaterIndices(data.Next, delta);
            }
            /*
            var data = _map[occupation];
            var newData = new PreviousNext(data.Prev, data.Next, data.Index + delta);
            _map = _map.SetItem(occupation, newData);

            ChangeLaterIndices(GetNext(occupation), delta);
            */
        }

        public Arc Remove(MachineOccupation toRemove)
        {
            Assert(JobSequenceChecks,
                toRemove.MachineId == _machine,
                $"{nameof(JobSequence)}: machine occupation, id mismatch.");
            Assert(JobSequenceChecks,
                _map.ContainsKey(toRemove),
                $"{nameof(JobSequence)}: machine id to remove not present, count={_map.Count}.");

            MachineOccupation prev = _map[toRemove].Prev;
            MachineOccupation next = _map[toRemove].Next;

            // prev <-x- toRemove -x-> next
            bool success = _map.Remove(toRemove);
            Assert(JobSequenceChecks, success);

            // prev <-- next
            if (next != null)
            {
                //_map = _map.SetItem(next, new PreviousNext(prev, _map[next].Next, _map[next].Index));
                _map[next].Prev = prev;

                ChangeLaterIndices(next, -1);
                Disconnect(toRemove, next);
            }

            // prev --> next
            if (prev != null)
            {
                //_map = _map.SetItem(prev, new PreviousNext(_map[prev].Prev, next, _map[prev].Index));
                _map[prev].Next = next;
                
                Disconnect(prev, toRemove);
            }

            if (_first.Equals(toRemove)) _first = next;
            if (_last.Equals(toRemove)) _last = prev;

            AssertClassInvariant();

            var insertedArc = (prev == null || next == null) ? default : Connect(prev, next);

            return insertedArc;
        }

        public Arc InsertFront(MachineOccupation toAdd)
        {
            if (_first == null)
            {
                Assert(JobSequenceChecks, _last == null);
                _map.Add(toAdd, new PreviousNext(null, null, 0));

                _first = toAdd;
                _last = toAdd;

                AssertClassInvariant();
                return default;
            }

            _map[_first].Prev = toAdd;
            ChangeLaterIndices(_first, +1);
            
            var arc = Connect(toAdd, _first);
            
            _map.Add(toAdd, new PreviousNext(null, _first, 0));
            _first = toAdd;

            AssertClassInvariant();
            return arc;
        }


        public (Arc, Arc) InsertAfter(MachineOccupation previous, MachineOccupation toAdd)
        {
            Assert(JobSequenceChecks,
                previous != null
                && toAdd != null
                && _map.ContainsKey(previous)
                && !_map.ContainsKey(toAdd));

            // ReSharper disable once AssignNullToNotNullAttribute (checked in the assert)
            MachineOccupation next = _map[previous].Next;

            // prev <-- new --> next
            // ReSharper disable once AssignNullToNotNullAttribute (checked in the assert)
            _map.Add(toAdd, new PreviousNext(previous, next, _map[previous].Index + 1));
            
            // prev --> new
            _map[previous].Next = toAdd;
            Arc arc0 = Connect(previous, toAdd);


            //  new <-- next
            Arc arc1 = default;
            if (next != null)
            {
                Assert(JobSequenceChecks, !_last.Equals(previous));
                _map[next].Prev = toAdd;
                ChangeLaterIndices(next, +1);
                arc1 = Connect(toAdd, next);
                Disconnect(previous, next);
            }

            if (_last.Equals(previous)) _last = toAdd;

            AssertClassInvariant();

            return (arc0, arc1);
        }


        public (Arc, Arc, Arc) Swap (MachineOccupation first, MachineOccupation next)
        {
            /*
             * first = occ1, next = occ2
             * 
             *                         to Remove
             *                         |
             *       occ0 ---->  occ1 ---->  occ2 ----> occ3
             *                    |          |            ^
             *                    |          is removed   |
             *                    |_______________________|
             *                              | 
             *                              arc inserted by removing occ2
             *
             *
             *             arcs inserted by re-adding occ2
             *             ______|______
             *             |           |
             *             |          new = arc[1]
             *             |           |
             *       occ0 ----> occ2 ----> occ1 ----> occ3
             *             |                     |
             *             present before        present before
             *             (transitive)          (transitive)
             *              = arc[0]              = arc[2]
             * 
             */

            AssertClassInvariant();

            Assert(JobSequenceChecks,
                first != null
                && next != null
                && _map.ContainsKey(first)
                && _map.ContainsKey(next));

            // ReSharper disable once AssignNullToNotNullAttribute (checked in the assert)
            var firstData = _map[first];

            // ReSharper disable once AssignNullToNotNullAttribute (checked in the assert)
            var nextData = _map[next];

            // ... -x-> first -x-> next -x-> ...
            if (firstData.Prev != null) Disconnect(firstData.Prev, first);
            Disconnect(first, next);
            if (nextData.Next != null) Disconnect(next, nextData.Next);

            // ... --> next --> first --> ...
            var arc0 = firstData.Prev != null ? Connect(firstData.Prev, next) : default;
            var arc1 = Connect(next, first);
            var arc2 = nextData.Next != null ? Connect(first, nextData.Next) : default;

            if (_first.Equals(first)) _first = next;
            if (_last.Equals(next)) _last = first;
            
            _map[next] = new PreviousNext(firstData.Prev, first, firstData.Index);
            _map[first] = new PreviousNext(next, nextData.Next, nextData.Index);

            if (firstData.Prev != null)
            {
                _map[firstData.Prev].Next = next;
            }

            if (nextData.Next != null)
            {
                _map[nextData.Next].Prev = first;
            }
            
            AssertClassInvariant();

            return (arc0, arc1, arc2);
        }

        /// <summary> </summary>
        /// <returns>The next occupation or null iff 'occupation' == Last()</returns>
        [Pure]
        public MachineOccupation GetNext(MachineOccupation occupation) => _map[occupation].Next;

        [Pure]
        public MachineOccupation GetPrevious(MachineOccupation occupation) => _map[occupation].Prev;

        [Pure]
        public MachineOccupation First() => _first;

        [Pure]
        public MachineOccupation Last() => _last;

        [Pure]
        public bool TransitiveArcExist(MachineOccupation source, MachineOccupation target)
        {
            // this is possible due to changed routes:
            if (!_map.ContainsKey(source) 
                || !_map.ContainsKey(target)) return false;

            return _map[source].Index < _map[target].Index;
        }

        [Pure]
        public double GetMissedConnectionPenalty()
        {
            if (Count < 2) return 0;

            if (_first.Type == MachineOccupationType.ConnectionOrigin
                && _last.Type == MachineOccupationType.ConnectionTarget)
            {
                return 0;
            }

            Assert(JobSequenceChecks, _first.Type == MachineOccupationType.ConnectionTarget
                                      && _last.Type == MachineOccupationType.ConnectionOrigin);

            var shouldBeEntryTimeTarget = Solution.GetEntryTime(_last.LastOperation) + _last.ReleaseTime;
            var actualEntryTimeTarget = Solution.GetEntryTime(_first.FirstOperation);
            var penalty = 5 + 10 * (shouldBeEntryTimeTarget.TotalMinutes - actualEntryTimeTarget.TotalMinutes);

            Assert(JobSequenceChecks, shouldBeEntryTimeTarget.Ticks < actualEntryTimeTarget.Ticks
                                      && penalty <= 0);

            return penalty;
        }

        [Pure]
        public IEnumerator<MachineOccupation> GetEnumerator()
        {
            for (MachineOccupation current = _first;
                current != null;
                current = _map[current].Next)

                yield return current;
        }

        [Pure]
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [Pure]
        public IJobSequence Clone(IGraphLayer newGraphLayer)
        {
            Dictionary<MachineOccupation, PreviousNext> copy = 
                // MachineOccupations is immutable, PreviousNext is mutable.
                _map.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone());
            
            return new JobSequence(copy, _machine, _first, _last, newGraphLayer);
        }

        [Pure]
        public bool Equals(IJobSequence other)
        {
            if (ReferenceEquals(null, other)) return false;

            if (ReferenceEquals(this, other)) return true;

            if (!(other is JobSequence)) throw new Exception();

            var o = other as JobSequence;

            if (o.Count != Count) return false;
            
            foreach (var kvp in _map)
            {
                if (o._map.TryGetValue(kvp.Key, out var oVal))
                {
                    if (kvp.Value.Index != oVal.Index) return false;
                }
                else return false;
            }

            return true;
        }

        [Pure]
        public override string ToString() => string.Join(" -> ", this);

        [Pure]
        public override int GetHashCode()
        {
            int CircularBitShift(int x, int n) => x << n | x >> (8 * sizeof(int) - n);

            int code = 0;

            foreach (var (m, i) in this.Numbered()) code ^= CircularBitShift(m.JobIndex, 5 * i);

            return code;
        }
    }
}
using System;

namespace SbbChallenge.Helpers
{

    public enum MachineOccupationType
    {
        Normal,
        ConnectionOrigin,   // where passengers get off
        ConnectionTarget,   // where passengers get on
    }

    /// <summary>
    /// MachineOccupation models the continued usage of a single machine by multiple (or one) consecutive operations
    /// of the same route. This is a core data type of the algorithm, as to obtain a solution, for each machine we have
    /// to find a permutation of all MachineOccupations of this machine.
    ///
    /// Note, that the arc inserted into the disjunctive graph for 2 MachineOccupations A, B which are consecutive in
    /// said permutation is exactly A.LastOperation + 1 --> B.FirstOperation. (Here, +1 comes from the *blocking*-job
    /// shop, ie. the machine is free for B, when A started processing on the successor Operation) 
    /// </summary>
    public class MachineOccupation : IEquatable<MachineOccupation>
    {
        // An immutable data type
        public readonly int FirstOperation;
        public readonly int LastOperation;
        public readonly int MachineId;
        
        public readonly int JobIndex;
        public readonly MachineOccupationType Type;  // Only necessary when modeling connections.
        public readonly TimeSpan ReleaseTime;

        public MachineOccupation(
            int firstOperation, int lastOperation, int machineId, int jobIndex, 
            MachineOccupationType type, TimeSpan releaseTime)
        {
            FirstOperation = firstOperation;
            LastOperation = lastOperation;
            MachineId = machineId;
            JobIndex = jobIndex;
            Type = type;
            ReleaseTime = releaseTime;
        }

        public bool Equals(MachineOccupation other)
        {
            return other != null
                   && FirstOperation == other.FirstOperation
                   && LastOperation == other.LastOperation
                   && MachineId == other.MachineId;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((MachineOccupation) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = FirstOperation;
                hashCode = (hashCode * 397) ^ LastOperation;
                hashCode = (hashCode * 397) ^ MachineId;
                return hashCode;
            }
        }

        public override string ToString()
        {
            return $"[{FirstOperation}..{LastOperation}]@{MachineId}";
        }
    }
}
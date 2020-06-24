using System;
using sbbChallange.ProblemDefinition;
using SbbChallenge.Helpers;

namespace sbbChallange.Layers
{
    public enum MoveType
    {
        RemoveCriticalArcLeftClosure, 
        RemoveCriticalArcRightClosure,
        ChangeRouting,
        ChangeRouteWithHeuristicInsertion,
        Reload,
    }
    
    /// <summary>
    /// Immutable datatype which represents a way a valid solution (Job shop layer) can be modified into a neighbour
    /// valid solution.
    /// </summary>
    public class Move : IEquatable<Move>
    {
        public readonly MoveType Type;
        public readonly Arc CriticalArc;
        public readonly Job JobToRouteSwap;
        public readonly Route RouteToInsert;
        
        // while not necessary to execute the move, we need it to create a taboo, ie not move back to the old route choice
        public readonly Route RouteToRemove;
         

        public static Move LeftClosure(Arc toRemove) => 
            new Move(MoveType.RemoveCriticalArcLeftClosure, toRemove, null, null, null);

        public static Move RightClosure(Arc toRemove) => 
            new Move(MoveType.RemoveCriticalArcRightClosure, toRemove, null, null, null);

        public static Move RouteSwap(Job jobToRouteSwap, Route routeToInsert, Route routeToRemove) => 
            new Move(MoveType.ChangeRouting, default, jobToRouteSwap, routeToInsert, routeToRemove);

        public static Move Reload(Job jobToReload) => 
            new Move(MoveType.Reload, default, jobToReload, null, null);

        public static Move ChangeRouteWithHeuristicInsertion(Job jobToRouteSwap, Route routeToInsert, Route routeToRemove) => 
            new Move(MoveType.ChangeRouting, default, jobToRouteSwap, routeToInsert, routeToRemove);

        private Move(
            MoveType type, 
            Arc critical, 
            Job jobToRouteSwap, Route routeToInsert, Route routeToRemove)
        {
            Type = type;
            CriticalArc = critical;
            JobToRouteSwap = jobToRouteSwap;
            RouteToInsert = routeToInsert;
            RouteToRemove = routeToRemove;
        }
        
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = 0;
                hashCode = (hashCode * 397) ^ (int) Type;
                hashCode = (hashCode * 397) ^ CriticalArc.GetHashCode();
                hashCode = (hashCode * 397) ^ (JobToRouteSwap != null ? JobToRouteSwap.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (RouteToInsert != null ? RouteToInsert.GetHashCode() : 0);
                return hashCode;
            }
        }

        public bool Equals(Move other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Type == other.Type 
                   && CriticalArc.Equals(other.CriticalArc) 
                   && Equals(JobToRouteSwap, other.JobToRouteSwap) 
                   && Equals(RouteToInsert, other.RouteToInsert) 
                   && Equals(RouteToRemove, other.RouteToRemove);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((Move) obj);
        }
    }
}
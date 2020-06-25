using System;
using System.Collections.Generic;
using System.Linq;

using sbbChallange.ProblemDefinition;
using SbbChallenge.Helpers;
using sbbChallange.IntegrityChecks;
using sbbChallange.Mutable.IntegrityChecks;

using static sbbChallange.IntegrityChecks.Asserts;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch RoutingChecks = 
            new Switch(nameof(Layers.RoutingLayer) + "." + nameof(RoutingChecks), 
                "After clone recalculated penalty must match the current routing penalty.",
                ".... some undocumented stuff...");
        
        
        public static readonly Switch AfterRouteLoadAcyclic = 
            new Switch(nameof(Layers.RoutingLayer) + "." + nameof(AfterRouteLoadAcyclic), 
                "After loading a route and calling the resp. left closures, the graph must be acyclic.");
    }
}

namespace sbbChallange.Layers
{
    public class RoutingLayer : 
        IRoutingLayer
    {
        private Problem Problem => Solution.Problem;
        private ISolution Solution => CostLayer.Solution;
        private ICostLayer CostLayer => GraphLayer.CostLayer;
        private IGraphLayer GraphLayer => SequencingLayer.GraphLayer;
        private ISequencingLayer SequencingLayer => ClosureLayer.SequencingLayer;
        
        public IClosureLayer ClosureLayer { get; }
        public double GetRoutingPenalty() => _penalty;

        private double _penalty; // = 0;
        
        public RoutingLayer (IClosureLayer closureLayer)
        {
            ClosureLayer = closureLayer;
            _penalty = Problem.Jobs.Select(j => Solution.GetRoute(j, canBeNull: true)?.RoutingPenalty ?? 0.0).Sum();
        }
        
        public IRoutingLayer Clone()
        {
            var res = new RoutingLayer(ClosureLayer.Clone());
            Assert(RoutingChecks, Math.Abs(_penalty - res._penalty) < 0.01);
            return res;
        }


        private Arc InsertSingleOccupation (MachineOccupation occupation, out Arc otherInsertedArc)
        {
            var mId = occupation.MachineId;
            
            var latestEntryNoDelay = Solution.GetOperation(occupation.LastOperation+1).LatestEntry;
            
            var next = SequencingLayer[mId]
                .FirstOrDefault(o =>
                    // release time of 'o'
                    Solution.GetEntryTime(o.FirstOperation)
                    > latestEntryNoDelay);
            
            // CASE 0: insert into an empty list.
            if (SequencingLayer[mId].Count == 0)
            {
                otherInsertedArc = SequencingLayer[mId].InsertFront(occupation); // hence, no disjunctive arcs are inserted (no arcs inserted)
                return default;
            }
            
            // CASE 1: insert at the end.
            if (next == null) // || next.Equals(SequencingLayer[mId].Last()))
            {
                // left closure has to be done with inserted arcs whose tail is in the job, hence none of those. (a1 == default)
                var (a0, a1) = SequencingLayer[mId].InsertAfter(SequencingLayer[mId].Last(), occupation);
                Assert(RoutingChecks, a1.Equals(default));
                otherInsertedArc = a0;
                //allInsertedArcs.Add(a0); allInsertedArcs.Add(a1);
                return default;
            }

            // CASE 2: insert at the front.
            if (SequencingLayer[mId].First().Equals(next))
            {
                var a0 = SequencingLayer[mId].InsertFront(occupation);
                otherInsertedArc = default;
                return a0;
            }
                
            // CASE 3: insert in the middle.
            else
            {
                var before = SequencingLayer[mId].GetPrevious(next);
                var (a0, a1) = SequencingLayer[mId].InsertAfter(before, occupation);
                    
                //allInsertedArcs.Add(a0); allInsertedArcs.Add(a1);
                Assert(RoutingChecks, !a1.Equals(default(Arc)) && !a0.Equals(default(Arc)));
                otherInsertedArc = a0;
                return a1;
            }
        }

        public void SetRoute(Job job, Route route)
        {
            Route oldRoute = Solution.GetRoute(job, canBeNull: true);

            Assert(RoutingChecks, Problem.Contains(job));
            Assert(RoutingChecks, !ReferenceEquals(oldRoute, route) || !oldRoute.Equals(route));
            
            GraphLayer.UpdateTimes();
            
            var oldOccupations = oldRoute
                                     ?.Operations.SelectMany(o => o.EndingMachineOccupations).ToArray()
                                 ?? new MachineOccupation[0];
            
            var newOccupations = route.Operations.SelectMany(o => o.EndingMachineOccupations).ToArray();

            // remove occupations no longer used:
            foreach (var occ in oldOccupations.Except(newOccupations))
            { 
                SequencingLayer[occ.MachineId].Remove(occ);
            }
            
            GraphLayer.SetRoute(job, route);
            _penalty -= oldRoute?.RoutingPenalty ?? 0.0;
            _penalty += route.RoutingPenalty;
            
            Queue<Arc> needsToComputeClosure = new Queue<Arc>();
            
            foreach (var occ in newOccupations.Except(oldOccupations))
            {
                var arc = InsertSingleOccupation(occ, out var otherArc);

                if (!arc.Equals(default))
                {
                    Assert(RoutingChecks, GraphLayer.ArcExists(arc));
                    needsToComputeClosure.Enqueue(arc);
                }
            }

            foreach (var occ in newOccupations.Intersect(oldOccupations))
            {
                var next = SequencingLayer[occ.MachineId].GetNext(occ);
                if (next != null)
                {
                    Arc a = new Arc(occ.LastOperation + 1, next.FirstOperation, TimeSpan.Zero, occ.MachineId);
                    Assert(RoutingChecks, GraphLayer.ArcExists(a));
                    needsToComputeClosure.Enqueue(a);
                }
            }
            
            ClosureLayer.LeftClosure(needsToComputeClosure);

            // Finally, Checks:
            Assert(AfterRouteLoadAcyclic, GraphLayer.IsAcyclic());

            (GraphLayer as GraphLayer)?.ClassInvariant();

            GraphMachinesIntegrity.Check(Problem, Solution, GraphLayer, SequencingLayer);

            GraphLayer.UpdateTimes();

            TimesGraphIntegrity.DoCheck(GraphLayer);
        }



        public bool Equals(IRoutingLayer other)
        {
            if (ReferenceEquals(other, null)) return false;

            if (ReferenceEquals(this, other)) return true;

            bool result = ClosureLayer.Equals(other.ClosureLayer);

            if (result)
            {
                Assert(RoutingChecks,
                    FloatingPointComparer.Instance.Equals(GetRoutingPenalty(), other.GetRoutingPenalty()),
                    "Two equal RoutingLayers must have matching routing penalties.");
            }
            
            return result;
        }


        public IEnumerable<Move> GetRoutingPenaltyReducingMoves()
        {
            foreach (Job job in Problem.Jobs.Where(j => Solution.GetRoute(j).RoutingPenalty > 0))
            {
                double currentPenalty = Solution.GetRoute(job).RoutingPenalty;
                foreach (Route route in job.Routes.Where(r => r.RoutingPenalty < currentPenalty))
                {
                    yield return Move.RouteSwap(job, route, Solution.GetRoute(job));
                }
            }
        }
        

        /// <summary>
        /// A <see cref="MachineOccupation"/> directly before or after an critical arc of the same machine, can reasonably be called
        /// 'critical' MachineOccupation, as the cost of the schedule without this occupation is guaranteed to be lower.
        /// This is the motivation to search Occupations of this type, and to then search for Routes that do not occupy
        /// the machine in question. This function does so and returns the respective 'Moves' (Solution changes) in
        /// decreasing route penalty order. 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Move> GetMachineAvoidingRouteSwaps()
        {
            foreach (var (occ, _) in GetCriticalOccupations().ToArray())
            {
                Job job = Solution.GetJob(occ.FirstOperation);
                if (!job.MachinesUsedByAllRoutes.Contains(occ.MachineId))
                {
                    // there must be a route, which does not contain the machine of the critical arc
                    Route route = job
                        .Where(r => r.SelectMany(o => o.MachineIds).Contains(occ.MachineId))
                        .OrderBy(r => r.RoutingPenalty)
                        .First();

                    if (!ReferenceEquals(route, Solution.GetRoute(job)))
                    {
                        yield return Move.RouteSwap(job, route, Solution.GetRoute(job));
                    }
                }
            }
        }

        /// <summary>
        /// Conceptually the route changing moves of <see cref="GetMachineAvoidingRouteSwaps"/> should be preferred over
        /// the ones computed by this function. However, we might not find enough/any and need the expand the search
        /// space. The Idea of the present method is simple: An <see cref="MachineOccupation"/> directly before and related to a
        /// critical arc leads to a delay that could be reduced if we moved this occupation forward within the schedule:
        /// Hence we choose a route were the given machine is occupied as early as possible. The converse is the case
        /// for MachineOccupations directly after a critical arc.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Move> GetAdditionalRouteSwaps()
        {
            const int numberOfRoutesPerCriticalOcc = Int32.MaxValue;
            
            foreach (var (occupation, type) in GetCriticalOccupations())
            {
                Job job = Solution.GetJob(occupation.FirstOperation);

                if (job.MachinesUsedByAllRoutes.Contains(occupation.MachineId))
                {
                    IEnumerable<Route> routes;
                    if (type == CriticalOccupationType.BeforeCriticalArc)
                    {
                        // the route selected is the route where the entryTime of the occupation on the machine in question
                        // is the earliest.
                        routes = job
                            .OrderBy(
                                r => r.First(
                                        o => o
                                            .StartingMachineOccupations
                                            .Select(m => m.MachineId).Contains(occupation.MachineId))
                                    .EarliestEarliestEntry)
                            .Take(numberOfRoutesPerCriticalOcc);
                    }
                    else
                    {
                        // type == CriticalOccupationType.AfterCriticalArc
                        // reverse of above (ie latest).
                        routes = job
                            .OrderByDescending(
                                r => r.First(
                                        o => o
                                            .StartingMachineOccupations
                                            .Select(m => m.MachineId).Contains(occupation.MachineId))
                                    .EarliestEarliestEntry)
                            .Take(numberOfRoutesPerCriticalOcc);
                    }

                    foreach (Route route in routes
                        .Where(r => !ReferenceEquals(r, Solution.GetRoute(job))))
                    {
                        yield return Move.RouteSwap(job, route, Solution.GetRoute(job));
                    }
                }
            }
        }
        
        private enum CriticalOccupationType { BeforeCriticalArc, AfterCriticalArc }

        /// <summary>  </summary>
        /// <returns>All distinct MachineOccupations directly before or after an critical disjunctive arc</returns>
        private (MachineOccupation, CriticalOccupationType)[] GetCriticalOccupations()
        {
            IEnumerable<(MachineOccupation, CriticalOccupationType)> ActualCode()
            {
                var (startingVertices, tree) = GraphLayer.GetCriticalTree();

                foreach (int start in startingVertices)
                foreach (var (from, to) in GraphLayer.BackTrackTree(start, tree).Reverse().Pairwise())
                {
                    Arc arc = GraphLayer.OutgoingArcs(from).First(a => a.Head == to);

                    if (arc.MachineId < 0) continue;

                    MachineOccupation occ0 = Solution
                        .GetOperation(@from - 1).EndingMachineOccupations
                        .First(o => o.MachineId == arc.MachineId);

                    yield return (occ0, CriticalOccupationType.BeforeCriticalArc);

                    MachineOccupation occ1 = Solution
                        .GetOperation(to).StartingMachineOccupations
                        .First(o => o.MachineId == arc.MachineId);

                    yield return (occ1, CriticalOccupationType.AfterCriticalArc);
                }
            }

            return ActualCode().Distinct().ToArray();
        }
        
        
        private int RoutingHash ()
        {
            // I designed this code with incremental updates in mind:
            //  assume we have the code cached. consider the update of
            //  this[i]: p -> p'
            //  then, code = cachedCode ^ (p.hash << i | ...) ^ (p'.hash << i | ...)
            //  since ^ is its own inverse and commutative.
            var code = 0;
            foreach (var (j, r) in Problem.Select(j => (j, Solution.GetRoute(j))))
            {

                //int h = 0;
                int h = r?.GetHashCode() ?? 0;
                //      circular bitshift:
                code ^= h << (5 * j.Id) | h >> ((sizeof(int) * 8) - (5 * j.Id));
            }
            return code;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (RoutingHash() * 397) ^ ClosureLayer.GetHashCode();
            }
        }
    }
}
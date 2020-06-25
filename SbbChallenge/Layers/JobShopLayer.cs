using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using sbbChallange.ProblemDefinition;
using SbbChallenge.Helpers;

using static sbbChallange.IntegrityChecks.Asserts;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch JobShopChecks = 
            new Switch(nameof(Layers.JobShopLayer) + "." + nameof(JobShopChecks),
                "Attempting to create arc swap moves only when delay cost > 0.", 
                "Route swap moves: routes must be different.",
                "Transitive arcs exist result must match with a slow path search.");

    }
}

namespace sbbChallange.Layers
{
    public class JobShopLayer : IJobShopLayer
    {
        private readonly IRoutingLayer _routingLayer;

        private IClosureLayer ClosureLayer => _routingLayer.ClosureLayer;
        private ISequencingLayer SequencingLayer => ClosureLayer.SequencingLayer;
        private IGraphLayer GraphLayer => SequencingLayer.GraphLayer;
        private ICostLayer CostLayer => GraphLayer.CostLayer;
        private ISolution Solution => CostLayer.Solution;
        private Problem Problem => Solution.Problem;
        
        private JobShopLayer (IRoutingLayer routingLayer)
        {
            _routingLayer = routingLayer;
        }

        public JobShopLayer(Problem problem)
        {
            _routingLayer =
                new RoutingLayer(
                    new ClosureLayer(
                        new SequencingLayer(
                            new GraphLayer(
                                new CostLayer(
                                    new Solution(problem))))));
        }


        public bool Equals(IJobShopLayer other)
        {
            return other != null 
                   && _routingLayer.Equals(other.RoutingLayer);
        }

        public IRoutingLayer RoutingLayer => _routingLayer;
        
        public double GetTotalCost() => GetDelayCost() + GetRoutingCost() + GetConnectionsCost();

        public double GetDelayCost()
        {
            GraphLayer.UpdateTimes();
            return CostLayer.GetDelayCost();
        }
        
        public double GetRoutingCost() => _routingLayer.GetRoutingPenalty();

        public double GetConnectionsCost() => SequencingLayer.GetMissedConnectionPenalty();


        public IEnumerable<Move> GetCriticalArcSwapMoves(CancellationToken cTok)
        {
            Assert(JobShopChecks, GetDelayCost() > 0);
            
            var criticalArcs = GraphLayer.GetCriticalArcs();

            foreach (Arc arc in criticalArcs)
            {
                yield return Move.LeftClosure(arc);
                yield return Move.RightClosure(arc);
            }
        }
        
        public IEnumerable<Move> GetRouteSwapHeuristicInsertionMoves (CancellationToken cTok)
        {
            var swapsThatRemoveOccupation = _routingLayer.GetMachineAvoidingRouteSwaps();
            var moreSwaps = _routingLayer.GetAdditionalRouteSwaps();
            
            var all = swapsThatRemoveOccupation.Concat(moreSwaps).ToArray();

            return all
                .Select(m => Move.ChangeRouteWithHeuristicInsertion(m.JobToRouteSwap, m.RouteToInsert, m.RouteToRemove))
                .ToArray();
        }
        

        public IEnumerable<Move> GetRouteSwapMoves (CancellationToken cTok)
        {
            var swapsThatRemoveOccupation = _routingLayer.GetMachineAvoidingRouteSwaps();
            var moreSwaps = _routingLayer.GetAdditionalRouteSwaps();

            var all = swapsThatRemoveOccupation.Concat(moreSwaps).ToArray();

            Assert(
                JobShopChecks,
                all.All(m => !ReferenceEquals(m.RouteToInsert, CostLayer.Solution.GetRoute(m.JobToRouteSwap))),
                "Substitute routes to swap in must be different from the ones present.");
            return all;
        }

        public IEnumerable<Move> Reloads()
        {
            return Problem.Select(Move.Reload);
        }

        public Move[] GetMoves(
            bool connectionFix,
            bool connectionImprovement,
            bool criticalArcsBased,
            bool routeSwapBased,
            bool routeSwapWithHeuristicInsertion,
            bool jobReinsertion,
            bool routePenaltyImprovement)
        {
            IEnumerable<Move> moves = new Move[0];

            if (connectionFix)
            {
                var arcs = SequencingLayer.GetConnectionCriticalArcs().ToArray();
                moves = moves.Concat(arcs.Select(Move.LeftClosure));
            //    moves = moves.Concat(arcs.Select(Move.RightClosure));
            }

            if (connectionImprovement)
            {
                var missedConnectionSequences = SequencingLayer.GetMissedConnectionSequences();
                var indicesOfPseudoCost = missedConnectionSequences.Select(s => s.Last().FirstOperation);
                var arcs = GraphLayer.GetCriticalArcs(indicesOfPseudoCost, onlyDisjunctive: true);
                moves = moves.Concat(arcs.Select(Move.LeftClosure));
            }

            if (criticalArcsBased)
            {
                moves = moves.Concat(GetCriticalArcSwapMoves(CancellationToken.None));
            }

            if (routeSwapBased && Problem.RoutingOptionsExist)
            {
                moves = moves.Concat(GetRouteSwapMoves(CancellationToken.None));
            }

            if (routeSwapWithHeuristicInsertion && Problem.RoutingOptionsExist)
            {
                moves = moves.Concat(GetRouteSwapHeuristicInsertionMoves(CancellationToken.None));
            }
            
            if (routePenaltyImprovement && Problem.RoutingOptionsExist)
            {
                moves = moves.Concat(_routingLayer.GetRoutingPenaltyReducingMoves());
            }

            return moves.ToArray();
        }

        public bool TransitiveArcExist(Arc arc)
        {
            MachineOccupation ending = Solution
                .GetOperation(arc.Tail - 1)
                .EndingMachineOccupations
                .FirstOrDefault(occ => occ.MachineId == arc.MachineId);

            MachineOccupation starting = Solution
                .GetOperation(arc.Head)
                .StartingMachineOccupations
                .FirstOrDefault(occ => occ.MachineId == arc.MachineId);

            // guess this is possible due to changed routes...
            if (starting == null || ending == null) return false;
            
            var result = SequencingLayer.TransitiveArcExist(ending, starting);

            #if DEBUG
            if (JobShopChecks.On)
            {
                var checkedResult = Graph.PathExists(
                    RoutingLayer.ClosureLayer.SequencingLayer.GraphLayer,
                    arc.Tail,
                    arc.Head);
                
                Assert(JobShopChecks, result == checkedResult,
                    "A transitive arc check must return the same result as a simple dfs path search.");
            }
            #endif
            return result;
        }


        public void ExecuteMove(Move move)
        {
            switch (move.Type)
            {
                case MoveType.RemoveCriticalArcLeftClosure:
                    Assert(JobSequenceChecks, GraphLayer.ArcExists(move.CriticalArc));
                    ClosureLayer.LeftClosureWithRemoval(move.CriticalArc);
                    Assert(JobSequenceChecks, !GraphLayer.ArcExists(move.CriticalArc));
                    break;
                
                case MoveType.RemoveCriticalArcRightClosure:
                    Assert(JobSequenceChecks, GraphLayer.ArcExists(move.CriticalArc));
                    ClosureLayer.RightClosureWithRemoval(move.CriticalArc);
                    Assert(JobSequenceChecks, !GraphLayer.ArcExists(move.CriticalArc));
                    break;
                
                case MoveType.ChangeRouting:
                    RoutingLayer.SetRoute(move.JobToRouteSwap, move.RouteToInsert);
                    break;
                
                case MoveType.Reload:
                    var route = Solution.GetRoute(move.JobToRouteSwap);
                    RoutingLayer.SetRoute(move.JobToRouteSwap, null);
                    RoutingLayer.SetRoute(move.JobToRouteSwap, route);
                    break;
                
                case MoveType.ChangeRouteWithHeuristicInsertion:
                    RoutingLayer.SetRoute(move.JobToRouteSwap, null);
                    RoutingLayer.SetRoute(move.JobToRouteSwap, move.RouteToInsert);
                    break;
                
                default: 
                    throw new Exception();
            }
        }

        public void CreateInitialSolution(
            IEnumerable<(Job, Route)> routing, 
            bool verbose)
        {
            foreach (var ((j, r), i) in routing.Numbered())
            {
                if (verbose) Console.Write($"Loading job {i}... ");
                
                _routingLayer.SetRoute(j, r);
                
                if (verbose) Console.Write($"done. ");
                
                if (verbose) Console.WriteLine($"(Cost={Math.Round(GetTotalCost(), 2)})");
            }
        }

        public IJobShopLayer Clone() => new JobShopLayer(_routingLayer.Clone());

        protected bool Equals(JobShopLayer other)
        {
            return _routingLayer.Equals(other._routingLayer);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((JobShopLayer) obj);
        }

        public override int GetHashCode() => _routingLayer.GetHashCode();
    }
}
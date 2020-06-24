using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using sbbChallange.ProblemDefinition;
using SbbChallenge.Helpers;
using sbbChallange.Layers;

using static sbbChallange.IntegrityChecks.Asserts;

namespace sbbChallange.Mutable.Search
{
    public class TabooList : ITabooList
    {
        public static readonly Switch Checks = new Switch(nameof(TabooList) + "." + nameof(Checks));
        
        public const int ArcTabooLength = 4; // if any of those are set to '0', we treat the resp. taboo type as disabled
        public const int RouteTabooLength = 5; 

        private readonly List<Arc> _prohibitedArcs;
        private readonly Dictionary<Job, HashSet<Route>> _prohibitedRoutes;
        public readonly HashSet<IJobShopLayer> _prohibitedSolutions;
        
        public string GetProhibitedArcsInfo() => 
            "[" + string.Join(" ", _prohibitedArcs) + "]";

        public string GetProhibitedRouteInto() =>
            "[" + string.Join(" ", _prohibitedRoutes.Select(kvp => $"{{ Job={kvp.Key.Id}, Count={kvp.Value.Count} }}"))  + "]";
        
        public TabooList()
        {
            _prohibitedArcs = new List<Arc>();
            _prohibitedRoutes = new Dictionary<Job, HashSet<Route>>();
            _prohibitedSolutions = new HashSet<IJobShopLayer>();
        }

        [Pure]
        public bool Any() => _prohibitedArcs.Any()
                             || _prohibitedRoutes.Any();

        public void ProhibitUndoOfMove(IJobShopLayer solution, Move move)
        {
            if (ArcTabooLength > 0
                && (move.Type == MoveType.RemoveCriticalArcLeftClosure
                    || move.Type == MoveType.RemoveCriticalArcRightClosure))
            {
                Assert(Checks, !_prohibitedArcs.Contains(move.CriticalArc));

                _prohibitedArcs.Add(move.CriticalArc);

                while (_prohibitedArcs.Count > ArcTabooLength) _prohibitedArcs.RemoveAt(0);
            }

            if (RouteTabooLength > 0
                && move.Type == MoveType.ChangeRouting)
            {
                if (_prohibitedRoutes.TryGetValue(move.JobToRouteSwap, out var set))
                    set.Add(move.RouteToRemove);

                else _prohibitedRoutes.Add(move.JobToRouteSwap, new HashSet<Route> {move.RouteToRemove});
            }

            _prohibitedSolutions.Add(solution.Clone());
        }

        public bool IsAprioriTaboo(Move move)
        {
            if (!Any()) return false;
            
            if (move.Type == MoveType.ChangeRouting 
                && _prohibitedRoutes.TryGetValue(move.JobToRouteSwap, out var set) 
                && set.Contains(move.RouteToInsert))
            {
                return true;
            }

            return false;
        }

        public bool IsAposterioriTaboo(IJobShopLayer solution)
        {
            if (!Any()) return false;

            if (_prohibitedSolutions.Contains(solution)) return true;

            foreach (Arc prohibitedArc in _prohibitedArcs)
            {
                if (solution.TransitiveArcExist(prohibitedArc)) return true;
            }
            
            // A route change cannot be aposteriori taboo, it would always be apriori taboo
            // (ie. we know its taboo before executing the move)
            return false;
        }

        public void Clear()
        {
            _prohibitedArcs.Clear();
            _prohibitedRoutes.Clear();
        }
    }
}
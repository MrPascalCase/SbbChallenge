using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Threading;
using sbbChallange.ProblemDefinition;
using SbbChallenge.Helpers;

namespace sbbChallange.Layers
{
    /// <summary>
    /// A Solution to a blocking job shop problem.
    /// - specifies for each job which route we should take.
    /// - specifies for each operation its entry time.
    /// </summary>
    public interface ISolution : 
        IEquatable<ISolution>
    {
        // immutable 'Problem' is the cached and optimized version of a IProblem. (see ProblemDefinition.IProblem)
        Problem Problem { get; }  
        
        [Pure] TimeSpan GetEntryTime (int operationId); // thesis: t_opId
        [Pure] TimeSpan GetMachineReleaseTime (MachineOccupation first, MachineOccupation second); // thesis: s_first
        
        void SetEntryTime(int operationId, TimeSpan timeSpan); // thesis: t_opId := timeSpan

        void SetRoute(Job job, Route route);
        
        /// <summary> Returns the job of a give operationId/vertexId. Done through a binary search => O(log(#jobs)).
        /// </summary>
        [Pure] Job GetJob(int operationId);
        
        /// <summary> If no route is set, getRoute return null if canBeNull==true, else throws an exception.</summary>
        [Pure] Route GetRoute(Job job, bool canBeNull = false);
        
        /// <summary> If no route is set, getRoute return null if canBeNull==true, else throws an exception.</summary>
        [Pure] Route GetRoute(int operationId, bool canBeNull = false);
        
        /// <summary> If no route is set, or the present route is too short, getOperation returns null if
        /// canBeNull==true, else throws an exception.</summary>
        [Pure] Operation GetOperation(int operationId, bool canBeNull = false);

        [Pure] ISolution Clone();
    }
    
    

    /// <summary>
    /// Caches only the DELAY cost. Routing penalty is handled in the routing layer. A soft penalty for missed
    /// connections is applied in the sequencing layer
    /// </summary>
    public interface ICostLayer : 
        IReadOnlyList<double>, 
        IEquatable<ICostLayer>
    {
        [Pure] ISolution Solution { get; }
        
        // thesis: Sum_{all o in O} w_o * T_o
        //     or  Max_{all o in O} w_o * T_o
        // depending on the objective function defined in Solution.Problem.Objective
        [Pure] double GetDelayCost (); 
        
        /// <summary>  Forwards the call to the Solution Layer, but keeps track of the cost at this operation, as well
        /// at the total cost. </summary>
        void SetEntryTime (int operation, TimeSpan newTime);
        
        void SetRoute(Job job, Route route);
        
        // thesis w_opId * T_opId
        [Pure] double GetCostAtOperation(int operationId);
        [Pure] ICostLayer Clone();
    }
    
    

    public interface IGraph : 
        IEquatable<IGraph>
    {
        void AddArc (int tail, int head, int machineId, TimeSpan length);
        void RemoveArc(int tail, int head, int machineId);
        
        [Pure] bool ArcExists(int tail, int head, int machineId, out TimeSpan length);
        [Pure] IEnumerable<Arc> OutgoingArcs(int vertex);
        [Pure] IEnumerable<Arc> IncomingArcs(int vertex);
        [Pure] int Count { get; }
        [Pure] IGraph Clone();
    }
    
    

    public static class GraphShortcuts
    {
        // IGraph extension methods:
        public static void AddArc(this IGraph graph, Arc arc) =>
            graph.AddArc(arc.Tail, arc.Head, arc.MachineId, arc.Length);

        public static void RemoveArc(this IGraph graph, Arc arc) =>
            graph.RemoveArc(arc.Tail, arc.Head, arc.MachineId);

        [Pure] public static bool ArcExists(this IGraph graph, int tail, int head, int machineId) =>
            graph.ArcExists(tail, head, machineId, out _);

        [Pure] public static bool ArcExists(this IGraph graph, Arc arc) =>
            graph.ArcExists(arc.Tail, arc.Head, arc.MachineId, out _);

        [Pure] public static bool ArcExists(this IGraph graph, Arc arc, out TimeSpan length) =>
            graph.ArcExists(arc.Tail, arc.Head, arc.MachineId, out length);
    }
    
    
    
    /// <summary>
    /// The graph layer takes care of updating the entry times. keeps track of adding/removeing arcs into the
    /// underlying scheduling graph. (see Thesis: 3.7 Update entry times; 1.4 Disjunctive graph;
    /// Definition 1.3 Critical arcs)
    /// </summary>
    public interface IGraphLayer : 
        IGraph, 
        IEquatable<IGraphLayer>
    {
        // Reference to the next lower level.
        ICostLayer CostLayer { get; }

        /// <summary>
        /// Internally the GraphLayer caches the modifications done.
        /// The update of the entry times is done in time dependant on the resulting modifications.
        /// (As opposed to always update all entry times.)
        /// This function triggers this update. (See thesis, 3.7 Updating the entry times)
        /// </summary>
        void UpdateTimes();
        [Pure] bool TimesAreUpToDate();

        void SetRoute (Job job, Route route);

        [Pure] IEnumerable<Arc> GetCriticalArcs (bool onlyDisjunctive = true);

        [Pure] IEnumerable<Arc> GetCriticalArcs(IEnumerable<int> backtrackFrom, bool onlyDisjunctive = true);
        
        [Pure] (int[], int[]) GetCriticalTree ();
        [Pure] IEnumerable<int> BackTrackTree (int from, int[] tree);

        [Pure] new IGraphLayer Clone();
    }
    
    
   
    public interface IJobSequence : 
        IReadOnlyCollection<MachineOccupation>, 
        IEquatable<IJobSequence>
    {
        Arc Remove (MachineOccupation toRemove);
        Arc InsertFront (MachineOccupation toAdd);
        (Arc, Arc) InsertAfter(MachineOccupation previous, MachineOccupation toAdd);
        (Arc, Arc, Arc) Swap (MachineOccupation first, MachineOccupation next);
        
        [Pure] MachineOccupation GetNext(MachineOccupation occupation);
        [Pure] MachineOccupation GetPrevious(MachineOccupation occupation);

        // allows more efficient implementation, instead of using the ones provided by LINQ
        [Pure] MachineOccupation First();
        [Pure] MachineOccupation Last();
        
        /// <summary> To check if a taboo arc exist as a transitive arc. </summary>
        [Pure] bool TransitiveArcExist (MachineOccupation source, MachineOccupation target);

        [Pure] double GetMissedConnectionPenalty();

        // The graphLayer is "owned" by the MachineLayer, but every machine permutation has to use that same graph.
        // Hence, when the MachineLayer clones (MachineLayer ~= list of all machine permutations), it is responsible 
        // for the cloning of the graphLayer. Hence when the MachineLayer then clones all the permutations, it has to 
        // give them access to the new GraphLayer reference. Hence the different signature of the Clone method below.
        [Pure] IJobSequence Clone(IGraphLayer newGraphLayer);
    }
    
    

    /// <summary>
    /// ISequencingLayer:
    /// Add/Remove MachineOccupation The central datatype with which the MachineLayer works is the MachineOccupation.
    /// This is a set of consecutive operations/vertices of a given route that use the same machine. For every machine,
    /// the Layer stores a permutation of the occupations. 
    /// The data structure used for this is a map : X -> (X.Previous, X.Next). In case of a tree map, this allows:
    /// - search                 O(lg n)
    /// - swap with neighbour    O(lg n)
    /// The disjunctive arcs in the graph are exclusively handled by the
    /// MachineLayer. 
    /// </summary>
    public interface ISequencingLayer :
        IReadOnlyList<IJobSequence>, 
        IEquatable<ISequencingLayer>
    {
        // Reference to the next lower level.
        [Pure] IGraphLayer GraphLayer { get; }
        
        /// <summary> To check if a taboo arc exist as a transitive arc. </summary>
        [Pure] bool TransitiveArcExist (MachineOccupation fst, MachineOccupation snd);
        
        [Pure] IEnumerable<IJobSequence> GetMissedConnectionSequences();
        
        [Pure] IEnumerable<Arc> GetConnectionCriticalArcs(); 

        [Pure] ISequencingLayer Clone();

        [Pure] double GetMissedConnectionPenalty();
    }
    
    

    public interface IClosureLayer : 
        IEquatable<IClosureLayer>
    {
        // Reference to the next lower level.
        [Pure] ISequencingLayer SequencingLayer { get; }

        void LeftClosure(IEnumerable<Arc> inserted);
        void LeftClosure(params Arc[] inserted);
        
        void LeftClosureWithRemoval(Arc toBeRemoved);
        void RightClosureWithRemoval(Arc toBeRemoved);

        [Pure] IClosureLayer Clone();
    }
    
    
    
    public interface IRoutingLayer :
        IEquatable<IRoutingLayer>
    {
        IClosureLayer ClosureLayer { get; }
        
        void SetRoute(Job job, Route route);

        [Pure] double GetRoutingPenalty();

        IEnumerable<Move> GetMachineAvoidingRouteSwaps();
        IEnumerable<Move> GetAdditionalRouteSwaps();
        IEnumerable<Move> GetRoutingPenaltyReducingMoves();

        IRoutingLayer Clone();
    }
    
    

    public interface ITabooList
    {
        [Pure] bool Any();

        void ProhibitUndoOfMove(IJobShopLayer solution, Move move);

        /// <summary>
        /// A move is 'apriori' taboo iff we would return to a route that is already prohibited.
        /// </summary>
        /// <remarks>
        /// Here we check moves before we execute them (hence, apriori). This is only possible with the
        /// route swaps, since we already know beforehand what the route selection will be like. On the other hand,
        /// with prohibited arcs we do not know if a resulting solution will include a prohibited arc before
        /// actually computing the resulting solution.
        /// </remarks>
        [Pure] bool IsAprioriTaboo(Move move);

        [Pure] bool IsAposterioriTaboo(IJobShopLayer solution);

        void Clear();
        
        // to display some runtime stats.
        [Pure] string GetProhibitedArcsInfo();
        [Pure] string GetProhibitedRouteInto();
    }
    

    
    public interface IJobShopLayer :
        IEquatable<IJobShopLayer>
    {
        // Reference to the next lower level.
        [Pure] IRoutingLayer RoutingLayer { get; }

        [Pure] double GetTotalCost();
        [Pure] double GetDelayCost();
        [Pure] double GetRoutingCost();
        [Pure] double GetConnectionsCost();

        /// <summary> </summary>
        /// <param name="connectionFix">Moves which force a connections `from' train to wait for the `onto' train</param>
        /// <param name="connectionImprovement">Moves which aim to reduce by how long the connection is missed, by scheduling a job earlier</param>
        /// <param name="criticalArcsBased">Normal moves</param>
        /// <param name="routeSwapBased">Moves which swap in a different route, trying to schedule the job as similarly as possible to how it was scheduled before</param>
        /// <param name="routeSwapWithHeuristicInsertion">Moves which swap in a different route, trying to schedule the job 'as late as possible, but in time'</param>
        /// <param name="jobReinsertion">Moves trying to reschedule a single job, 'as late as possible, but in time'</param>
        /// <param name="routePenaltyImprovement">Moves swapping routes with penalties, to routes with lower penalties</param>
        [Pure]
        Move[] GetMoves(
            bool connectionFix,
            bool connectionImprovement,
            bool criticalArcsBased,
            bool routeSwapBased,
            bool routeSwapWithHeuristicInsertion,
            bool jobReinsertion,
            bool routePenaltyImprovement
        );
        
        // See Thesis, 3.1 Transitive arcs
        [Pure] bool TransitiveArcExist (Arc arc);
        
        void ExecuteMove (Move move);

        void CreateInitialSolution(IEnumerable<(Job, Route)> routing, bool verbose = true);
        
        [Pure] IJobShopLayer Clone();
    }
}
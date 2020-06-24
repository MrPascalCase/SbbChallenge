using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

using sbbChallange.ProblemDefinition;
using SbbChallenge.Helpers;
using sbbChallange.Mutable.IntegrityChecks;

using static sbbChallange.IntegrityChecks.Asserts;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch 
            GraphLayerChecks = new Switch(nameof(Layers.GraphLayer) + "." + nameof(GraphLayerChecks),
                "Critical node along a path has no predecessor <=> determined by arc from sigma.",
                "Adding arcs: head != tail, machine Id in valid range, if conjunctive => job(head) == job(tail).",
                "Note, disjunctive =X=> job(head) != job(tail) as we have reentrant jobs.");

        public static readonly Switch 
            GraphLayerLoopInvariant = new Switch(nameof(Layers.GraphLayer) + "." + nameof(GraphLayerLoopInvariant), 
                "Loop invariant of Update times: All entry times, topologically before a registered problem must be correct.");

        public static readonly Switch 
            GraphLayerClassInvariant = new Switch(nameof(Layers.GraphLayer) + "." + nameof(GraphLayerClassInvariant),
                "Forward/backward arcs must match.",
                "Conjunctive arcs must be present according to the solution (tails, heads, mIds, lengths).",
                "If there is no route loaded there cannot be any arcs adjacent to the resp vertices.");
    }
}


namespace sbbChallange.Layers
{
    public class GraphLayer : IGraphLayer
    {
        struct TimeAtVertex : IComparable<TimeAtVertex>
        {
            public readonly int Vertex;
            private readonly TimeSpan Time;

            public TimeAtVertex(int vertex, TimeSpan time)
            {
                Vertex = vertex;
                Time = time;
            }

            // This comparison is used to keep this structs on a heap, in order to process them in increasing time 
            //     order. However, if they have the same time, we still have to check for equality
            //     (ie, the vertex part), as we otherwise could not use a set. 
            //         (in case of equal time, and different vertex, the ordering returned does not matter,
            //         but needs to be consistent)
            public int CompareTo(TimeAtVertex other)
            {
                int timeComp = Time.CompareTo(other.Time);
                return timeComp != 0 ? timeComp : Vertex - other.Vertex;
            }
        }

        private Problem Problem => Solution.Problem;
        private ISolution Solution => CostLayer.Solution;
        private readonly IGraph _graph;
        public ICostLayer CostLayer { get; }

        /// <summary>
        /// To minimize the effort of keeping the entry times up to date, we only recompute them on the part of the
        /// graph that changed. NecessaryFixes keeps track of some changes: the crucial invariant is the following:
        ///
        ///    *all vertices that topologically succeed any vertex within
        ///                                   _necessaryFixes are allowed to have wrong entry times.*
        /// Equivalent:
        /// 
        /// For all v in G
        ///     (validTime (v)
        ///     OR (Exists u in Fixes AND Exists path{u -> v}))
        /// 
        /// </summary>
        private readonly SortedSet<TimeAtVertex> _necessaryFixes;

        public bool TimesAreUpToDate() => _necessaryFixes.Count == 0;

        /// <summary>
        /// Returns true is the conjunctive part is properly loaded. If a string builder is passed in, and problems are
        /// detected, a description of the problems is added to the StringBuilder.
        /// </summary>
        private bool ConjunctiveRouteProperlyLoaded(Route route, StringBuilder report = null)
        {
            #if DEBUG
            var result = true;
            foreach (var (op0, op1) in route.Operations.Pairwise())
            {
                if (!_graph.ArcExists(op0.Id, op1.Id, -1, out var l))
                {
                    result = false;
                    report?.AppendLine($"Arc {op0.Id} -> {op1.Id} missing.");
                }
                else if (l != op0.Runtime)
                {
                    result = false;
                    report?.AppendLine(
                        $"Arc {op0.Id} -> {op1.Id} should have length {op0.Runtime.Show()}, but has length {l.Show()}.");
                }
            }

            if (result) report?.AppendLine($"Route properly loaded.");

            return result;
            #endif
            return true;
        }

        /// <summary>
        /// Sets the route for a job, on the graph layer. Ie. insert/remove/change length of all conjunctive arcs,
        /// propagate the entry time changes into the cost layer and keep track the effects this has on the rest of
        /// the graph (by inserting into _necessaryFixes when necessary). Note, that it is not the graph layers job
        /// to handle machine occupations, but we do not want to delete and reinsert those that do not change
        /// (with a route swap). Hence, this method expects that all disjunctive arcs present (adjacent to the 'job')
        /// correspond to machine occupations that  do not change with the route swap.
        /// </summary>
        /// <param name="job">The job, whose route we set</param>
        /// <param name="route">The route to insert, null if the route of this job is to be removed.</param>
        public void SetRoute(Job job, Route route)
        {
            Route newRoute = route;
            Route oldRoute = Solution.GetRoute(job, canBeNull: true);

            Assert(GraphLayerChecks, !(oldRoute == null && newRoute == null));
            Assert(GraphLayerChecks, (oldRoute == null || job.Contains(oldRoute))
                                     && (newRoute == null || job.Contains(newRoute)));

            if (oldRoute != null)
            {
                var report = new StringBuilder();
                Assert(GraphLayerChecks, ConjunctiveRouteProperlyLoaded(oldRoute, report), report.ToString());
                //Assert(GraphLayerChecks, oldRoute.Pairwise((o0, o1) => ArcExists(o0.Id, o1.Id, -1, out _)).All(x => x));
            }

            // Precondition: 
            //
            // AdjacentArcs(job) == ArcsOf (Intersect (MachineOcc (newRoute), MachineOcc (oldRoute)))
            //
            // This is, all disjunctive arcs adjacent to the job in question, are disjunctive arcs of
            // machine occupations that occur in both routes.
            if (GraphLayerChecks.On)
            {
                // ReSharper disable once InvokeAsExtensionMethod (more readable)
                var relevantOccupations =
                    Enumerable.Intersect(
                        newRoute?.SelectMany(op => op.EndingMachineOccupations) ?? new MachineOccupation[0],
                        oldRoute?.SelectMany(op => op.EndingMachineOccupations) ?? new MachineOccupation[0]);

                IEnumerable<Arc> incomingDisjunctive =
                    Enumerable.Range(job.FirstOperation, job.LongestRoute).SelectMany(IncomingArcs)
                        .Where(a => a.MachineId != -1);

                IEnumerable<Arc> outgoingDisjunctive =
                    Enumerable.Range(job.FirstOperation, job.LongestRoute).SelectMany(OutgoingArcs)
                        .Where(a => a.MachineId != -1);

                Assert(GraphLayerChecks, incomingDisjunctive.All(a =>
                    relevantOccupations.Any(occ =>
                        occ.FirstOperation == a.Head && occ.MachineId == a.MachineId)));

                Assert(GraphLayerChecks, outgoingDisjunctive.All(a =>
                    relevantOccupations.Any(occ =>
                        occ.LastOperation == a.Tail - 1 && occ.MachineId == a.MachineId)));
            }
            // end precondition.

            // 1)  For conjunctive arcs:
            //     insert/delete/change length 
            //
            // 2)  Given the incoming disjunctive arcs and earliest entry times,
            //     update the entryTimes within the job
            //
            // 3)  For all leaving disjunctive arcs:
            //     If the head entry time changes, update and set a necessary fix.

            // Note, _graph.<someMethod>(...) bypasses the registration of the necessaryFixes.
            // (as opposed to this.<someMethod>())

            // Step 1:
            for (int i = 0; i < job.LongestRoute - 1; i++)
            {
                var oldOp = oldRoute == null || (oldRoute.Count - 1) <= i ? null : oldRoute[i];
                var newOp = newRoute == null || (newRoute.Count - 1) <= i ? null : newRoute[i];
                // On the '-1': arcs are inserted for the operations [0, ..., n-1] ; there is no arc for the
                // last (dummy) operation

                var vertexIndex = job.FirstOperation + i;
                var arc = new Arc(vertexIndex, vertexIndex + 1, newOp?.Runtime ?? TimeSpan.Zero, -1);

                if (newOp == null && oldOp == null)
                    // only if both routes are shorter than the longest route:
                    // oldRoute.length < i && newRoute.length < i, but there is a route longer than 'old' and 'new'
                {
                    Assert(GraphLayerChecks, oldRoute == null || vertexIndex > (oldRoute.Count - 1));
                    Assert(GraphLayerChecks, newRoute == null || vertexIndex > (newRoute.Count - 1));
                    break;
                }

                else if (newOp == null /* && oldOp != null */) _graph.RemoveArc(arc);

                else if ( /* newOp != null && */ oldOp == null) _graph.AddArc(arc);

                else /* if (newOp != null && oldOp != null) */
                if (oldOp.Runtime != newOp.Runtime)
                {
                    _graph.RemoveArc(arc);
                    _graph.AddArc(arc);
                }
            }

            CostLayer.SetRoute(job, route);

            // Step 2:
            TimeSpan ArcBasedIncomingTime(int index) =>
                IncomingArcs(index).Select(a => Solution.GetEntryTime(a.Tail) + a.Length).Prepend(TimeSpan.MinValue)
                    .Max();

            TimeSpan ShouldBeEntryTime(int index) =>
                (Solution.GetOperation(index, canBeNull: true)?.EarliestEarliestEntry ?? TimeSpan.Zero).Max(
                    ArcBasedIncomingTime(index));

            if (newRoute == null)
                // ReSharper disable once PossibleNullReferenceException (see assert above)
                foreach (var op in oldRoute)
                    CostLayer.SetEntryTime(op.Id, TimeSpan.Zero);

            else
                foreach (var op in newRoute)
                {
                    var time = ShouldBeEntryTime(op.Id);

                    if (Solution.GetEntryTime(op.Id) != time) CostLayer.SetEntryTime(op.Id, time);
                }


            // Step 3:
            if (newRoute != null)
            {
                var outgoingDisjunctive = Enumerable.Range(job.FirstOperation, newRoute.Count)
                    .SelectMany(OutgoingArcs).Where(a => a.MachineId != -1);

                foreach (Arc arc in outgoingDisjunctive)
                {
                    var shouldBe = ShouldBeEntryTime(arc.Head);
                    var actual = Solution.GetEntryTime(arc.Head);

                    if (actual != shouldBe)
                    {
                        CostLayer.SetEntryTime(arc.Head, shouldBe);
                        _necessaryFixes.Add(new TimeAtVertex(arc.Head, shouldBe));
                    }
                }
            }

            // Checks:
            if (newRoute != null && GraphLayerChecks.On)
            {
                StringBuilder report = new StringBuilder();
                Assert(GraphLayerChecks, ConjunctiveRouteProperlyLoaded(newRoute, report), report.ToString());
            }
        }

        public GraphLayer(ICostLayer costLayer)
        {
            CostLayer = costLayer;
            _graph = new Graph(Problem.OperationCount);
            _necessaryFixes = new SortedSet<TimeAtVertex>();
        }

        private GraphLayer(IGraph graph, ICostLayer costLayer)
        {
            _graph = graph;
            _necessaryFixes = new SortedSet<TimeAtVertex>();
            CostLayer = costLayer;
        }

        [Conditional("DEBUG")]
        private void UpdateTimesLoopInvariant()
        {
            if (!GraphLayerLoopInvariant.On) return;

            var set = _necessaryFixes.Select(tav => tav.Vertex).ToHashSet();

            foreach (int current in this.TopologicalOrder(
                starts: Enumerable.Range(0, Count).Where(v => OutgoingArcs(v).Any() && !IncomingArcs(v).Any()),
                breakingCondition: i => set.Contains(i)))
            {
                TimeSpan shouldBe = IncomingArcs(current)
                    .Select(a => Solution.GetEntryTime(a.Tail) + a.Length)
                    .Append(TimeSpan.Zero)
                    .Max();

                Operation op = Solution.GetOperation(current);
                shouldBe = shouldBe.Max(op.EarliestEarliestEntry);

                Assert(
                    GraphLayerLoopInvariant,
                    Solution.GetEntryTime(current) == shouldBe,
                    $"Times should be equal:\n" +
                    $"\tRecorded = {Solution.GetEntryTime(current)}\n" +
                    $"\tRecalculated = {shouldBe}\n");
            }
        }

        public void UpdateTimes(CancellationToken cTok)
        {
            if (_necessaryFixes.Count == 0) return;

            ClassInvariant();
            UpdateTimesLoopInvariant();

            while (_necessaryFixes.Count > 0)
            {
                if (cTok != default && cTok.IsCancellationRequested) return;

                int current = _necessaryFixes.Min.Vertex;
                _necessaryFixes.Remove(_necessaryFixes.Min);

                foreach (Arc arc in OutgoingArcs(current))
                {
                    if (Solution.GetEntryTime(arc.Head) < Solution.GetEntryTime(arc.Tail) + arc.Length)
                    {
                        CostLayer.SetEntryTime(arc.Head, Solution.GetEntryTime(arc.Tail) + arc.Length);
                        _necessaryFixes.Add(new TimeAtVertex(arc.Head, Solution.GetEntryTime(arc.Tail) + arc.Length));
                    }

                    else if (Solution.GetEntryTime(arc.Head) > Solution.GetEntryTime(arc.Tail) + arc.Length)
                    {
                        TimeSpan oldTime = Solution.GetEntryTime(arc.Head);
                        TimeSpan newTime = IncomingArcs(arc.Head)
                            .Select(a => Solution.GetEntryTime(a.Tail) + a.Length)
                            .Append(Solution.GetOperation(arc.Head).EarliestEarliestEntry)
                            .Max();

                        if (oldTime != newTime)
                        {
                            CostLayer.SetEntryTime(arc.Head, newTime);
                            _necessaryFixes.Add(new TimeAtVertex(arc.Head, newTime));
                        }
                    }
                }

                UpdateTimesLoopInvariant();
            }

            ClassInvariant();
            TimesGraphIntegrity.DoCheck(this);
        }


        public IEnumerable<Arc> GetCriticalArcs(bool onlyDisjunctive = true)
        {
            var objective = Problem.Objective;

            var indicesOfCosts = Enumerable
                    .Range(0, _graph.Count)
                    .Where(i => CostLayer[i] > 0)
                    .OrderByDescending(i => CostLayer[i])
                    .Take(objective == ObjectiveType.MaximumWeighedTardiness ? 1 : Int32.MaxValue)
                ;

            return GetCriticalArcs(indicesOfCosts, onlyDisjunctive);
        }

        public IEnumerable<Arc> GetCriticalArcs(IEnumerable<int> backtrackFrom, bool onlyDisjunctive = true)
        {
            if (!TimesAreUpToDate()) throw new Exception();

            TimesGraphIntegrity.DoCheck(this);
            ClassInvariant();

            // used iff obj == max weighted tardiness
            HashSet<int> alreadyReturned = new HashSet<int>();

            var indicesOfCosts = backtrackFrom;

            foreach (int vertex in indicesOfCosts)
            {
                int current = vertex;

                while (true)
                {
                    if (alreadyReturned.Contains(current)) break;

                    alreadyReturned.Add(current);

                    if (!_graph.IncomingArcs(current).Any())
                    {
                        break;
                    }

                    Arc arc = _graph.IncomingArcs(current)
                        .FirstOrDefault(a => Solution.GetEntryTime(a.Head) == Solution.GetEntryTime(a.Tail) + a.Length);

                    if (arc.Equals(default))
                    {
                        // can only be the case of the entry time of current is determined by an arc form 'sigma':
                        if (Solution.GetOperation(current).EarliestEarliestEntry == Solution.GetEntryTime(current))
                        {
                            break;
                        }
                        else
                        {
                            throw new Exception("\n----------------------------------------------------------------\n" +
                                                TimesGraphIntegrity.CreateDebugMessageOnDetectedTimeInconsistency(this,
                                                    current) +
                                                "\n----------------------------------------------------------------\n");
                        }
                    }

                    if (arc.MachineId >= 0 || !onlyDisjunctive) yield return arc;

                    current = arc.Tail;
                }
            }
        }

        public (int[], int[]) GetCriticalTree()
        {
            if (!TimesAreUpToDate()) throw new Exception("Cannot compute critical tree if times are not up to date.");

            TimesGraphIntegrity.DoCheck(this);

            int[] startVertices = Enumerable.Range(0, Count).Where(i => CostLayer[i] > 0).ToArray();

            Stack<int> stack = new Stack<int>();
            foreach (int v in startVertices)
            {
                stack.Push(v);
            }

            int[] treeArray = new int[Count];
            for (int i = 0; i < Count; i++)
            {
                treeArray[i] = -1;
            }

            while (stack.Count > 0)
            {
                int current = stack.Pop();

                Arc tightArc = _graph.IncomingArcs(current).FirstOrDefault(
                    a => Solution.GetEntryTime(a.Head) == Solution.GetEntryTime(a.Tail) + a.Length);

                if (tightArc.Equals(default))
                {
                    // in this case, the entry time at current should be determined by an 'arc' from 'sigma'
                    // (I dont save them in the graph but just query them form the operation)
                    Assert(GraphLayerChecks,
                        Solution.GetOperation(current).EarliestEarliestEntry == Solution.GetEntryTime(current),
                        TimesGraphIntegrity.CreateDebugMessageOnDetectedTimeInconsistency(this, current));
                    // treeArray[current] := -1, current is the start of a critical path.
                    continue;
                }

                treeArray[current] = tightArc.Tail;

                // if we do not arrive at a vertex from which we backtracked before
                if (treeArray[tightArc.Tail] == -1) stack.Push(tightArc.Tail);
            }

            return (startVertices, treeArray);
        }

        public string ShowTree(int[] tree)
        {
            List<int>[] reverseTree = new List<int>[Count];

            for (int i = 0; i < tree.Length; i++)
                if (tree[i] != -1)
                {
                    if (reverseTree[tree[i]] == null) reverseTree[tree[i]] = new List<int>();

                    reverseTree[tree[i]].Add(i);
                }

            StringBuilder builder = new StringBuilder();

            void Dfs(int location, string prefix, StringBuilder output)
            {
                output.AppendLine(prefix + location);
                if (reverseTree[location] != null)
                    foreach (var i in reverseTree[location])
                    {
                        Dfs(i, prefix + "  ", output);
                    }
            }

            for (int i = 0; i < reverseTree.Length; i++)
            {
                if (tree[i] == -1 && reverseTree[i] != null && reverseTree[i].Count > 0)
                {
                    // Starting location:
                    Dfs(i, "", builder);
                }
            }

            return builder.ToString();
        }

        public IEnumerable<int> BackTrackTree(int from, int[] tree)
        {
            for (; tree[from] != -1; from = tree[from]) yield return from;
        }

        public IEnumerable<(Job, Route)> GetRouteSwaps(MachineOccupation occ)
        {
            var job = Solution.GetJob(occ.FirstOperation);

            foreach (Route route in job)
            {
                var allMachines = route.SelectMany(o => o.MachineIds);

                if (!allMachines.Contains(occ.MachineId))
                {
                    yield return (job, route);
                }
            }
        }

        public void AddArc(int tail, int head, int machineId, TimeSpan length)
        {
            #if DEBUG
            {
                Assert(GraphLayerChecks, tail != head);
                Assert(GraphLayerChecks, -1 <= machineId && machineId < Problem.MachineAndConnectionCount);
                Assert(GraphLayerChecks, !this.ArcExists(tail, head, machineId));

                if (machineId >= 0)
                {
                    // below is not the case, as we have reentrant jobs                    
                    //Assert(FastChecks, !ReferenceEquals(Solution.GetJob(tail), Solution.GetJob(head)));
                }
                else
                {
                    Assert(GraphLayerChecks, ReferenceEquals(Solution.GetJob(tail), Solution.GetJob(head)));
                }
            }
            #endif

            _graph.AddArc(tail, head, machineId, length);

            TimeSpan old = Solution.GetEntryTime(head);
            TimeSpan newTime = Solution.GetEntryTime(tail) + length;

            if (newTime > old)
            {
                CostLayer.SetEntryTime(head, newTime);
                _necessaryFixes.Add(new TimeAtVertex(head, newTime));
            }
        }


        public void RemoveArc(int tail, int head, int machineId)
        {
            _graph.RemoveArc(tail, head, machineId);

            TimeSpan oldTime = Solution.GetEntryTime(head);
            TimeSpan newTime = _graph
                .IncomingArcs(head)
                .Select(a => Solution.GetEntryTime(a.Tail) + a.Length)
                .Append(Solution.GetOperation(head).EarliestEarliestEntry)
                .Max();

            if (newTime < oldTime)
            {
                CostLayer.SetEntryTime(head, newTime);
                _necessaryFixes.Add(new TimeAtVertex(head, newTime));
            }
        }

        public bool ArcExists(int tail, int head, int machineId, out TimeSpan length) =>
            _graph.ArcExists(tail, head, machineId, out length);

        public IEnumerable<Arc> OutgoingArcs(int vertex) => _graph.OutgoingArcs(vertex);

        public IEnumerable<Arc> IncomingArcs(int vertex) => _graph.IncomingArcs(vertex);

        public int Count => _graph.Count;

        IGraph IGraph.Clone() => _graph.Clone();

        public bool Equals(IGraph other)
            => this.IsEquivalentTo(other);

        public bool Equals(IGraphLayer other)
        {
            if (other == null) return false;

            if (ReferenceEquals(this, other)) return true;

            IEnumerable<TimeAtVertex> SymmetricDifference(
                IEnumerable<TimeAtVertex> fst,
                IEnumerable<TimeAtVertex> snd)
            {
                return fst.Except(snd).Concat(snd.Except(fst));
            }

            if (other is GraphLayer gLayer && !SymmetricDifference(_necessaryFixes, gLayer._necessaryFixes).Any())
            {
                // both have the same necessary fixes:
                return this.IsEquivalentTo(other) && CostLayer.Equals(other.CostLayer);
            }

            UpdateTimes(default);
            other.UpdateTimes();

            return this.IsEquivalentTo(other) && CostLayer.Equals(other.CostLayer);
        }

        public override bool Equals(object other)
            => this.IsEquivalentTo((IGraph) other);

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }

        public IGraphLayer Clone()
        {
            if (!TimesAreUpToDate()) throw new Exception();

            UpdateTimes(default);
            Assert(GraphLayerChecks, _necessaryFixes.Count == 0);

            return new GraphLayer(_graph.Clone(), CostLayer.Clone());
        }

        [Conditional("DEBUG")]
        public void ClassInvariant()
        {
            if (!GraphLayerClassInvariant.On) return;

            // forward-,  backward arcs match
            for (int i = 0; i < Count; i++)
            {
                foreach (Arc arc in OutgoingArcs(i))
                {
                    Assert(GraphLayerClassInvariant, IncomingArcs(arc.Head)
                        .Contains(arc, GraphMethods.WithLengthArcComparer.Instance));
                }
            }

            foreach (Job job in Problem)
            {
                if (Solution.GetRoute(job, canBeNull: true) != null)
                {
                    // all conjunctive arcs are where they are supposed to be
                    foreach (var (op0, op1) in Solution.GetRoute(job).Pairwise())
                        Assert(GraphLayerClassInvariant,
                            ArcExists(op0.Id, op1.Id, -1, out var length) && length == op0.Runtime);
                }
                else
                {
                    // all vertices of a job are disconnected if no route is loaded
                    for (int i = job.FirstOperation; i <= /*!!*/ job.LastOperation; i++)
                        Assert(GraphLayerClassInvariant, !OutgoingArcs(i).Any() && !IncomingArcs(i).Any());
                }
            }

            // disjunctive arcs between trains, conjunctive arcs within trains
            for (int i = 0; i < Count; i++)
            {
                foreach (Arc arc in OutgoingArcs(i))
                {
                    if (arc.MachineId >= 0)
                    {
                        // The following assert has to be disabled when we allow reentrant jobs! 

                        // disjunctive: different trains 
                        // Assert(ClassInvariantCheck, !ReferenceEquals(Solution.GetJob(arc.Head), Solution.GetJob(arc.Tail)));
                    }
                    else
                    {
                        // conjunctive: same train
                        Assert(GraphLayerClassInvariant,
                            ReferenceEquals(Solution.GetJob(arc.Head), Solution.GetJob(arc.Tail)));
                    }
                }
            }
        }

    }
}
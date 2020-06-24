using System;
using System.Collections.Generic;
using System.Linq;
using sbbChallange.ProblemDefinition;
using SbbChallenge.Helpers;
using sbbChallange.Layers;
using sbbChallange.Visualization;

using static sbbChallange.IntegrityChecks.Asserts;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch
            BeforeRemovalAcyclic =
                new Switch(nameof(ClosureLayer) + "." + nameof(BeforeRemovalAcyclic),
                    "assert that the resp. graph is acyclic before left/right closure WITH REMOVAL of the critical arc."),

            InvertPrecedenceCheck =
                new Switch(nameof(ClosureLayer) + "." + nameof(InvertPrecedenceCheck),
                    "arcs to be removed must exist",
                    "direct neighbours in the sequencing layer must match",
                    "arcs reported by the sequencing layer must exist"),

            ClosureCheck =
                new Switch(nameof(ClosureLayer) + "." + nameof(ClosureCheck),
                    "fast checks about reachableFrom[] array within left/right closure"),

            AfterRemoveAcyclic =
                new Switch(nameof(ClosureLayer) + "." + nameof(AfterRemoveAcyclic), 
                    "assert that the resp. graph is acyclic after left/right closure WITH REMOVAL of the critical arc.");
    }
}


namespace sbbChallange.Layers
{
    public class ClosureLayer : IClosureLayer
    {   
        private IGraphLayer GraphLayer => SequencingLayer.GraphLayer;
        private ISolution Solution => GraphLayer.CostLayer.Solution;
        public ISequencingLayer SequencingLayer { get; }

        public ClosureLayer(ISequencingLayer sequencingLayer)
        {
            SequencingLayer = sequencingLayer;
        }

        [System.Diagnostics.Contracts.Pure]
        public IClosureLayer Clone() => new ClosureLayer(SequencingLayer.Clone());


        public void LeftClosureWithRemoval(Arc toBeRemoved)
        {
            Assert(InvertPrecedenceCheck, GraphLayer.ArcExists(toBeRemoved.Tail, toBeRemoved.Head, toBeRemoved.MachineId));
            Assert(BeforeRemovalAcyclic, GraphLayer.IsAcyclic());
            
            if (!GraphLayer.TimesAreUpToDate()) throw new Exception();
             
            
            Route route = Solution.GetRoute(toBeRemoved.Head);
            var criterion = route.Operations
                                .SelectMany(op => GraphLayer.OutgoingArcs(op.Id))
                                .Select(a => a.Head)
                                .Distinct()
                                .Select(v => Solution.GetEntryTime(v))
                                .Max() + TimeSpan.FromTicks(1);
            
            var arcs = SwapInMate(toBeRemoved);
            
            var otherCriterion = route.Operations
                                .SelectMany(op => GraphLayer.OutgoingArcs(op.Id))
                                .Select(a => a.Head)
                                .Distinct()
                                .Select(v => Solution.GetEntryTime(v))
                                .Max() + TimeSpan.FromTicks(1);

            Assert(InvertPrecedenceCheck, !GraphLayer.ArcExists(toBeRemoved.Tail, toBeRemoved.Head, toBeRemoved.MachineId));

            //var criterion = arcs.Select(a => Solution.GetEntryTime(a.Head) + Solution.GetOperation(a.Head).RunTime + TimeSpan.FromTicks(1)).Max();
            
            LeftClosure(arcs[1], useTerminationCriterion: true, criterion);
            
            Assert(InvertPrecedenceCheck, !GraphLayer.ArcExists(toBeRemoved.Tail, toBeRemoved.Head, toBeRemoved.MachineId));

            if (!GraphLayer.IsAcyclic())
            {
                Console.WriteLine($"Cycle detected, visualizing");
                Console.WriteLine($"Termination criterion = {criterion.Show()}");
                GraphLayer.Visualize(GraphVisualization.NeighbourSelector(GraphLayer, route, 2));                
            }
            
            Assert(AfterRemoveAcyclic, GraphLayer.IsAcyclic());
        }
        
        public void RightClosureWithRemoval(Arc toBeRemoved)
        {
            Assert(InvertPrecedenceCheck, GraphLayer.ArcExists(toBeRemoved.Tail, toBeRemoved.Head, toBeRemoved.MachineId));
            Assert(BeforeRemovalAcyclic, GraphLayer.IsAcyclic());
            
            var arcs = SwapInMate(toBeRemoved);
            
            Assert(InvertPrecedenceCheck, !GraphLayer.ArcExists(toBeRemoved.Tail, toBeRemoved.Head, toBeRemoved.MachineId));

            //var criterion = arcs.Select(a => Solution.GetEntryTime(a.Head) + Solution.GetOperation(a.Head).RunTime + TimeSpan.FromTicks(1)).Max();
            
            RightClosure(arcs[1], true, default);
            
            Assert(InvertPrecedenceCheck, !GraphLayer.ArcExists(toBeRemoved.Tail, toBeRemoved.Head, toBeRemoved.MachineId));
            Assert(AfterRemoveAcyclic, GraphLayer.IsAcyclic(), "GraphLayer.IsAcyclic()");
        }


        public void RightClosure(
            Arc inserted, 
            bool useTerminationCriterion, 
            TimeSpan manuallySetCriterion)
        {
            // 'inserted' is a arc which enters the currently focused job J. 
            // now we have to find a path in the *reversed graph* from inserted.tail into the front part of the job,
            // ie. [arc.head ... job.last].
            
            // in the left closure: inserted.Tail
            int lowestTrainVertex = Solution.GetJob(inserted.Head).FirstOperation;
            int highestTrainVertex = Solution.GetJob(inserted.Head).LastOperation;

            int[] reachableFrom = new int[GraphLayer.Count];

            for (int i = lowestTrainVertex; i <= highestTrainVertex; i++)
            {
                reachableFrom[i] = i + 1;    
                // Sad explanation for the +1 hack:
                // originally we had a vertex '0' modeling the sigma. but we removed this since all of the graph is 
                // much more uniform without the sigma.
                
                Assert(ClosureCheck, reachableFrom[i] > 0); // otherwise, we might terminate at a unvisited vertex.
            }
            
            
            TimeSpan terminationCriterion = manuallySetCriterion;
            if (useTerminationCriterion && manuallySetCriterion.Equals(default))
            {
                Route r = Solution.GetRoute(lowestTrainVertex);
                //terminationCriterion = Solution.GetEntryTime(r.Last().Id) + r.Last().RunTime + TimeSpan.FromTicks(1);

                terminationCriterion = r
                                           .Select(op => op.Id)
                                           .SelectMany(id => GraphLayer.IncomingArcs(id))
                                           .Select(a => a.Tail).Distinct()
                                           .Select(v => Solution.GetEntryTime(v))
                                           .Min()
                                       + TimeSpan.FromTicks(1);


            }
            

            // in the left closure: inserted.Tail
            int maxPriority = Solution.GetJob(inserted.Head).LongestRoute;
            BucketPriorityQueue collection = new BucketPriorityQueue(maxPriority: maxPriority + 1);
            
            // in the right closure priorities are reversed, ie a vertex occuring earlier in the train shall override
            // later ones. Therefore we will always insert with priority: (Max - LeftClosurePriority)
            collection.Insert(inserted, priority: maxPriority - (reachableFrom[inserted.Head] - lowestTrainVertex - 1));
            
            while (!collection.IsEmpty)
            {
                //CheckLoopInvariant(collection);
                
                Arc current = collection.Pop();
                
                Assert(ClosureCheck,reachableFrom[current.Head] > 0);
                
                if (useTerminationCriterion 
                    && Solution.GetEntryTime(current.Tail) < terminationCriterion)
                {
                    continue;
                }
                
                if (current.MachineId >= 0
                    && !GraphLayer.ArcExists(current.Tail, current.Head, current.MachineId))
                {
                    continue;
                }

                // As we now search backwards through the graph, we reenter the job with an arc whose tail is within
                // the job (!= left closure)
                if (lowestTrainVertex <= current.Tail && current.Tail <= highestTrainVertex
                    // The inequality is reversed, as the priories run the other way.
                    && reachableFrom[current.Head] <= current.Tail + 1)
                {
                    Arc[] arcs = SwapInMate(current); // current is removed from the graph.
                    // current was leaving the job, hence the 'arcs' are entering

                    foreach (Arc arc in arcs)
                    {
                        if (!arc.Equals(default(Arc)) && reachableFrom[arc.Head] != 0)
                        {
                            // again, insert with reversed priority
                            collection.Insert(arc, maxPriority - (reachableFrom[arc.Head] - lowestTrainVertex - 1));
                        }
                    }
                }

                // if the front (tail, as reversed) has a lower priority (higher value) we redo with the higher
                // priority (lower value)
                else if (reachableFrom[current.Tail] > reachableFrom[current.Head] || reachableFrom[current.Tail] == 0)
                         //&& reachableFrom[current.Head] != 0) // tail is not unvisited.
                {
                    reachableFrom[current.Tail] = reachableFrom[current.Head];
                    
                    foreach (Arc arc in GraphLayer.IncomingArcs(current.Tail))
                    {
                        Assert(ClosureCheck,reachableFrom[arc.Head] == reachableFrom[current.Head]);
                        collection.Insert(arc, maxPriority - (reachableFrom[arc.Head] - lowestTrainVertex - 1));
                    }
                }
            }
        }

        public void LeftClosure(params Arc[] inserted) => LeftClosure((IEnumerable<Arc>)inserted);

        public void LeftClosure(IEnumerable<Arc> inserted)
        {
            IEnumerable<Arc> enumeratedInput = inserted as Arc[] ?? inserted.ToArray();
            
            if (!enumeratedInput.Any()) return;

            var firstInserted = enumeratedInput.First();
            
            int lowestTrainVertex = Solution.GetJob(firstInserted.Tail).FirstOperation;
            int highestTrainVertex = Solution.GetJob(firstInserted.Tail).LastOperation;
            
            // all inserted arcs have the same tail job.
            Assert(ClosureCheck, enumeratedInput
                    .Select(i => Solution.GetJob(i.Tail))
                    .Aggregate(true, (b, job) => b && ReferenceEquals(job, Solution.GetJob(firstInserted.Tail))));

            int[] reachableFrom = new int[GraphLayer.Count];

            for (int i = lowestTrainVertex; i <= highestTrainVertex; i++)
            {
                reachableFrom[i] = i + 1;    
                // Sad explanation for the +1 hack:
                // originally we had a vertex '0' modeling the sigma. but we removed this since all of the graph is 
                // much more uniform without the sigma.
                
                Assert(ClosureCheck, reachableFrom[i] > 0); // otherwise, we might terminate at a unvisited vertex.
            }

            BucketPriorityQueue collection = new BucketPriorityQueue(Solution.GetJob(firstInserted.Tail).LongestRoute);

            foreach (Arc arc in enumeratedInput)
            {
                collection.Insert(arc, reachableFrom[arc.Tail] - lowestTrainVertex - 1);
            }
            
            Route r = Solution.GetRoute(lowestTrainVertex);
            TimeSpan terminationCriterion = r
                                           .Select(op => op.Id)
                                           .SelectMany(id => GraphLayer.OutgoingArcs(id))
                                           .Select(a => a.Head)
                                           .Distinct()
                                           .Select(v => Solution.GetEntryTime(v))
                                           .Max()
                                       + TimeSpan.FromTicks(1);
            
            while (!collection.IsEmpty)
            {
                Arc current = collection.Pop();

                if (Solution.GetEntryTime(current.Head) > terminationCriterion)
                {
                    continue;
                }

                if (current.MachineId >= 0
                    && !GraphLayer.ArcExists(current.Tail, current.Head, current.MachineId))
                {
                    continue;
                }

                if (lowestTrainVertex <= current.Head 
                    && current.Head <= highestTrainVertex
                    && reachableFrom[current.Tail] >= current.Head + 1)
                {
                    Arc[] arcs = SwapInMate(current); // current is removed from the graph.

                    foreach (Arc arc in arcs)
                        if (!arc.Equals(default)
                            && reachableFrom[arc.Tail] != 0)
                        {
                            collection.Insert(arc, reachableFrom[arc.Tail] - lowestTrainVertex - 1);
                        }
                }

                else if (reachableFrom[current.Tail] > reachableFrom[current.Head])
                {
                    reachableFrom[current.Head] = reachableFrom[current.Tail];
                    foreach (Arc arc in GraphLayer.OutgoingArcs(current.Head))
                    {
                        collection.Insert(arc, reachableFrom[arc.Tail] - lowestTrainVertex - 1);
                    }
                }
            }
        }
        

        /// <summary>
        /// 
        /// </summary>
        /// <param name="inserted"></param>
        /// <param name="useTerminationCriterion"></param>
        /// <param name="manuallySetCriterion">Should have a default value of 'default'. But, this is not possible
        /// as otherwise we cannot implement the respective interface. If the 'manuallySetCriterion' == default
        /// and 'useTerminationCriterion' == true, a termination criterion is used, that is based on the assumption,
        /// that a single leftClosure is computed. Ie, that the EntryTimes would be correct, except for the ones
        /// affected by that single inserted Arc.</param>
        // The operation is preformed with respect to the Job of 'INSERTED.TAIL' 
        public void LeftClosure(
            Arc inserted, 
            bool useTerminationCriterion, 
            TimeSpan manuallySetCriterion)
        {
            int lowestTrainVertex = Solution.GetJob(inserted.Tail).FirstOperation;
            int highestTrainVertex = Solution.GetJob(inserted.Tail).LastOperation;

            int[] reachableFrom = new int[GraphLayer.Count];

            for (int i = lowestTrainVertex; i <= highestTrainVertex; i++)
            {
                reachableFrom[i] = i + 1;    
                // Sad explanation for the +1 hack:
                // originally we had a vertex '0' modeling the sigma. but we removed this since all of the graph is 
                // much more uniform without the sigma. 0 is a valid operation id now, but we need 0 to signal that 
                // this vertex was not yet visited.
                
                Assert(ClosureCheck, reachableFrom[i] > 0); // otherwise, we might terminate at a unvisited vertex.
            }

            BucketPriorityQueue collection = new BucketPriorityQueue(Solution.GetJob(inserted.Tail).LongestRoute);
            collection.Insert(inserted, reachableFrom[inserted.Tail] - lowestTrainVertex - 1);

            
            TimeSpan terminationCriterion = manuallySetCriterion;
            if (useTerminationCriterion && manuallySetCriterion.Equals(default))
            {
                Route r = Solution.GetRoute(lowestTrainVertex);

                terminationCriterion = r
                                           .Select(op => op.Id)
                                           .SelectMany(id => GraphLayer.OutgoingArcs(id))
                                           .Select(a => a.Head).Distinct()
                                           .Select(v => Solution.GetEntryTime(v))
                                           .Max()
                                       + TimeSpan.FromTicks(1);


            }
            
            while (!collection.IsEmpty)
            {
                Arc current = collection.Pop();

                if (useTerminationCriterion 
                    && Solution.GetEntryTime(current.Head) > terminationCriterion)
                {
                    continue;
                }

                if (current.MachineId >= 0
                    && !GraphLayer.ArcExists(current.Tail, current.Head, current.MachineId))
                {
                    continue;
                }

                if (lowestTrainVertex <= current.Head && current.Head <= highestTrainVertex
                                                      && reachableFrom[current.Tail] >= current.Head + 1)
                {
                    Arc[] arcs = SwapInMate(current); // current is removed from the graph.

                    foreach (Arc arc in arcs)
                        if (!arc.Equals(default)
                            && reachableFrom[arc.Tail] != 0)
                        {
                            collection.Insert(arc, reachableFrom[arc.Tail] - lowestTrainVertex - 1);
                        }
                }

                else if (reachableFrom[current.Tail] > reachableFrom[current.Head])
                {
                    reachableFrom[current.Head] = reachableFrom[current.Tail];
                    foreach (Arc arc in GraphLayer.OutgoingArcs(current.Head))
                    {
                        collection.Insert(arc, reachableFrom[arc.Tail] - lowestTrainVertex - 1);
                    }
                }
            }
        }

        private Arc[] SwapInMate(Arc critical)
        {
            /*
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

            int mId = critical.MachineId;

            var startingOccupation = Solution
                .GetOperation(critical.Head)
                .StartingMachineOccupations
                .First(o => o.MachineId == critical.MachineId);

            var endingOccupation = Solution
                .GetOperation(critical.Tail - 1)
                .EndingMachineOccupations
                .First(o => o.MachineId == critical.MachineId);

            Assert(
                InvertPrecedenceCheck, 
                GraphLayer.ArcExists(critical.Tail, critical.Head, critical.MachineId));

            Assert(
                InvertPrecedenceCheck,
                SequencingLayer[mId].GetNext(endingOccupation).Equals(startingOccupation));

            var result_ = SequencingLayer[mId].Swap(endingOccupation, startingOccupation);
            var result = new[] {result_.Item1, result_.Item2, result_.Item3};

            Assert(
                InvertPrecedenceCheck,
                SequencingLayer[mId].GetNext(startingOccupation).Equals(endingOccupation));

            Assert(
                InvertPrecedenceCheck,
                result.All(a => a.Equals(default(Arc)) || GraphLayer.ArcExists(a.Tail, a.Head, a.MachineId)));

            Assert(InvertPrecedenceCheck,
                !GraphLayer.ArcExists(critical.Tail, critical.Head, critical.MachineId));

            return result;
        }

        public bool Equals(IClosureLayer other)
        {
            if (ReferenceEquals(other, null)) return false;

            if (ReferenceEquals(this, other)) return true;
            
            return SequencingLayer.Equals(other.SequencingLayer);
        }

        public override int GetHashCode() => SequencingLayer.GetHashCode();
    }
}
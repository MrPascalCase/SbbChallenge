using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;

using sbbChallange.ProblemDefinition;
using SbbChallenge.Helpers;

using static sbbChallange.IntegrityChecks.Asserts;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch SequencingChecks = 
            new Switch(nameof(Layers.SequencingLayer) + "." + nameof(SequencingChecks));
    }
}

namespace sbbChallange.Layers
{
    /// <summary>
    /// Contains only a debug helper method, which prints the differences between two job-sequences.
    /// </summary>
    public static class SequencingLayerExtensions
    {
        public static string ReportDifference (
            this ISequencingLayer first, ISequencingLayer second, StringBuilder builder = null)
        {
            if (builder == null) builder = new StringBuilder();

            if (first.Count != second.Count) throw new Exception();
           
           builder.AppendLine("Difference between sequencing layers:");

           for (int i = 0; i < first.Count; i++)
               ReportDifference(first[i], second[i], builder);
           
           builder.AppendLine("End of difference.");
           return builder.ToString();
        }

        public static string ReportDifference(
            this IJobSequence first, IJobSequence second, StringBuilder builder)
        {
            if (builder == null) builder = new StringBuilder();
            
            var array1 = first.ToArray();
            var array2 = second.ToArray();

            if (!array1.Any() || !array2.Any())
            {
                builder.AppendLine($"firstCount={array1.Length}, secondCount={array2.Length}");
                return builder.ToString();
            }
            
            if (array1.First().MachineId != array2.First().MachineId) throw new Exception();

            if (array1.SequenceEqual(array2))
            {
                builder.AppendLine($"Sequences equal M={array1.First().MachineId}");
                return builder.ToString();
            }

            Console.Write($"Sequences different M={array1.First().MachineId}  ");
            int minLength = Math.Min(array1.Length, array2.Length);
            for (int i = 0; i < minLength; i++)
                if (!array1[i].Equals(array2[i]))
                    builder.Append($"{{ i={i}: {array1[i]} vs {array2[i]} }}  ");

            int maxLength = Math.Max(array1.Length, array2.Length);
            for (int i = minLength; i < maxLength; i++)
                if (array1.Length > array2.Length)
                    builder.Append($"{{ i={i}: {array1[i]} vs nothing }}  ");
                else
                    builder.Append($"{{ i={i}: nothing vs {array2[i]} }}  ");

            builder.AppendLine();
            return builder.ToString();
        }
    }
    
    /// <summary>
    /// Models the order in which jobs are executed on all the machines.
    /// </summary>
    public class SequencingLayer : 
        ISequencingLayer
    {
        
        private readonly IJobSequence[] _sequences;
        private ISolution Solution => GraphLayer.CostLayer.Solution;
        private Problem Problem => Solution.Problem;
        
        public IGraphLayer GraphLayer { get; }
        
        
        /// <summary>
        /// Creates an empty MachineLayer; No disjunctive arcs shall be present.
        /// </summary>
        public SequencingLayer(IGraphLayer graphLayer)
        {
            GraphLayer = graphLayer;
            _sequences = new IJobSequence[Problem.MachineAndConnectionCount];
            for (int i = 0; i < Problem.MachineAndConnectionCount; i++)
            {
                _sequences[i] = new JobSequence(i, GraphLayer);
            }
        }
        
        private SequencingLayer(IJobSequence[] sequences, IGraphLayer graphLayer)
        {
            GraphLayer = graphLayer;
            _sequences = sequences;
        }

        [Pure]
        public bool TransitiveArcExist(MachineOccupation fst, MachineOccupation snd)
        {
            Assert(SequencingChecks, fst.MachineId == snd.MachineId);
            
            return _sequences[fst.MachineId].TransitiveArcExist(fst, snd);
        }

        [Pure]
        public IEnumerable<IJobSequence> GetMissedConnectionSequences()
        {
            for (int i = Problem.MachineCount;
                i < Problem.MachineAndConnectionCount;
                i++)
            {
                var seq = _sequences[i];
                if (seq.Count == 2 && seq.First().Type == MachineOccupationType.ConnectionTarget)
                {
                    yield return seq;
                }
            }
        }

        [Pure]
        public IEnumerable<Arc> GetConnectionCriticalArcs()
        {
            for (int i = Problem.MachineCount;
                i < Problem.MachineAndConnectionCount;
                i++)
            {
                var order = _sequences[i];
                if (order.Count <= 1) continue;
                
                Assert(SequencingChecks, order.Count == 2);
                Assert(SequencingChecks, order.All(occ => occ.Type != MachineOccupationType.Normal));

                if (order.First().Type == MachineOccupationType.ConnectionOrigin) continue;
                    
                Assert(SequencingChecks, 
                    order.First().Type == MachineOccupationType.ConnectionTarget
                    && order.Last().Type == MachineOccupationType.ConnectionOrigin);
                
                var arc = Arc.BetweenOccupations(order.First(), order.Last());

                Assert(SequencingChecks, GraphLayer.ArcExists(arc));
                
                yield return arc;
            }
        }


        public IEnumerator<IJobSequence> GetEnumerator()
            => ((IEnumerable<IJobSequence>) _sequences).GetEnumerator();
        
        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public int Count 
            => _sequences.Length;

        public IJobSequence this[int index] 
            => _sequences[index];

        [Pure]
        public ISequencingLayer Clone()
        {
            IGraphLayer graphClone = GraphLayer.Clone();
            return new SequencingLayer(
                _sequences.Select(p => p.Clone(graphClone)).ToArray(), 
                graphClone);
        }
        
        [Pure]
        public double GetMissedConnectionPenalty()
        {
            return Enumerable
                .Range(Problem.MachineCount, Problem.ConnectionCount)
                .Select(i => _sequences[i])
                .Where(s => s != null)
                .Select(s => s.GetMissedConnectionPenalty())
                .Sum();
            
        }

        [Pure]
        public bool Equals(ISequencingLayer other)
        {
            if (ReferenceEquals(other, null)) return false;

            bool result = ReferenceEquals(this, other) 
                          || (this.SequenceEqual(other) && GraphLayer.Equals(other.GraphLayer));
            
            return result;
        }

        [Pure]
        public override string ToString()
        {
            return _sequences.Numbered().Select(tuple => $"[{tuple.Item2}]\t{tuple.Item1}").JoinToString("\n");
        }

        [Pure]
        public override int GetHashCode()
        {
            // I designed this code with incremental updates in mind:
            //  assume we have the code cached. consider the update of
            //  this[i]: p -> p'
            //  then, code = cachedCode ^ (p.hash << i | ...) ^ (p'.hash << i | ...)
            //  since ^ is its own inverse and commutative.
            var code = 0;
            foreach (var (p, i) in _sequences.Numbered())
            {
                int h = p.GetHashCode();
                //      circular bitshift:
                code ^= h << (5 * i) | h >> ((sizeof(int) * 8) - (5 * i));
            }
            return code;
        }
    }
}
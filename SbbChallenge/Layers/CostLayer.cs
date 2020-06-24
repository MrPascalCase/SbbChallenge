using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SbbChallenge.Helpers;
using sbbChallange.ProblemDefinition;
using static sbbChallange.IntegrityChecks.Asserts;
using static System.Math;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch CostLayerChecks = 
            new Switch(nameof(Layers.CostLayer) + "." + nameof(CostLayerChecks), 
                "For set entry time: no penalty weight => cost=0");

        public static readonly Switch CostLayerClassInvariant =
            new Switch(nameof(Layers.CostLayer) + "." + nameof(CostLayerClassInvariant),
                "For all operations: no route => cost=0, time=0", 
                "For all operations: cost = max { delay * penalty weight, 0 }",
                "Cached cost = sum_i { cost[i] }");
    }
}

namespace sbbChallange.Layers
{
    public class CostLayer : ICostLayer
    {
        private Problem Problem => Solution.Problem;
        
        private readonly double[] _costs;
        public ISolution Solution { get; }
        private double _sumDelayCost;

        public double GetDelayCost() => 
            Problem.Objective == ObjectiveType.SumWeightedTardiness
                ? _sumDelayCost 
                : _costs.Max();
        

        public CostLayer(ISolution solution)
        {
            Solution = solution;
            _costs = new double[Problem.OperationCount];
        }

        public void SetEntryTime(int operation, TimeSpan newTime)
        {
            var delayWeight = Solution.GetOperation(operation).DelayWeight;
            if (delayWeight > 0)
            {
                var latestEntry = Solution.GetOperation(operation).LatestEntry;
                _sumDelayCost -= _costs[operation];
                
                if (latestEntry < newTime)
                {
                    _costs[operation] = Max((newTime - latestEntry).TotalMinutes * delayWeight, 0);
                    _sumDelayCost += _costs[operation];
                }
                
                else _costs[operation] = 0.0;
            }

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            // Is justified, as if the value is 0, it is supposed to be directly set as such and is not the result of a computation.
            else Assert(CostLayerChecks, _costs[operation] == 0.0);

            Solution.SetEntryTime(operation, newTime);
            
            AssertClassInvariant();
        }

        
        /// <summary>
        /// Adjusts the cost layer for a new route selection, ie. costs are adopted if the length of the route that the operations delay weight change. 
        /// </summary>
        public void SetRoute(Job job, Route route)
        {
            Solution.SetRoute(job, route);
            
            if (route != null)
            {
                foreach (var op in route)
                {
                    if (op.DelayWeight > 0)
                    {
                        _sumDelayCost -= _costs[op.Id];
                        _costs[op.Id] = Max((Solution.GetEntryTime(op.Id) - op.LatestEntry).TotalMinutes * op.DelayWeight, 0);
                        _sumDelayCost += _costs[op.Id];
                        
                    } 
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    else if (op.DelayWeight == 0 && _costs[op.Id] > 0)
                    {
                        _sumDelayCost -= _costs[op.Id];
                        _costs[op.Id] = 0.0;
                    }
                }

                
                for (int i = route[route.Count-1].Id + 1; i < job.FirstOperation + job.LongestRoute; i++)
                {
                    Assert(CostLayerChecks, Solution.GetOperation(i, true) == null);
                    _sumDelayCost -= _costs[i];
                    _costs[i] = 0.0;
                    Solution.SetEntryTime(i, TimeSpan.Zero);
                }
            }
            else
            {
                for (int i = job.FirstOperation; i < job.FirstOperation + job.LongestRoute; i++)
                {
                    _sumDelayCost -= _costs[i];
                    _costs[i] = 0.0;
                }
            }
            
            AssertClassInvariant();
        }

        public double GetCostAtOperation(int operationId) => this[operationId];

        [System.Diagnostics.Conditional("DEBUG")]
        private void AssertClassInvariant()
        {
            if (!CostLayerClassInvariant.On) return;
            
            const double epsilon = 10e-5;
            for (int i = 0; i < _costs.Length; i++)
            {
                var j = Solution.GetJob(i);
                // if no route is selected
                if (Solution.GetRoute(j, canBeNull: true) == null)
                {
                    Assert(CostLayerClassInvariant, Abs(_costs[i]) < epsilon);
                    Assert(CostLayerClassInvariant, Solution.GetEntryTime(i) == TimeSpan.Zero);
                }
                else
                // if a route is selected:
                {
                    // the current index is within the route; the route might be shorter than the allocated vertices
                    if (Solution.GetRoute(j).Count + Solution.GetJob(i).FirstOperation <= i)
                    {
                        Assert(CostLayerClassInvariant, Abs(_costs[i]) < epsilon);
                        Assert(CostLayerClassInvariant, Solution.GetEntryTime(i) == TimeSpan.Zero);
                    }
                    
                    // vertex i is actually associated to a route-vertex
                    else
                    {
                        var diff = Solution.GetOperation(i).DelayWeight *
                                   (Solution.GetEntryTime(i) - Solution.GetOperation(i).LatestEntry).TotalMinutes;

                        Assert(
                            CostLayerClassInvariant,
                            Abs(_costs[i] - Max(diff, 0)) < epsilon,
                            $"Cost mismatch at operation '{i}':\n" +
                            $"\tRecorded cost = {_costs[i]}\n" +
                            $"\tDelay weight = {Solution.GetOperation(i).DelayWeight}\n" +
                            $"\tLatest entry time = {Solution.GetOperation(i).LatestEntry}\n" +
                            $"\tActual entry time = {Solution.GetEntryTime(i)}\n");
                    }
                }
            }

            Assert(CostLayerClassInvariant, Abs(_sumDelayCost - _costs.Sum()) < epsilon);
        }

        public IEnumerator<double> GetEnumerator() => ((IEnumerable<double>) _costs).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int Count => _costs.Length;

        public double this[int index] => _costs[index];

        public ICostLayer Clone()
        {
            CostLayer newLayer = new CostLayer(Solution.Clone());
            Array.Copy(_costs, newLayer._costs, Count);
            newLayer._sumDelayCost = _sumDelayCost;
            return newLayer;
        }

        public bool Equals(ICostLayer other)
        {
            if (ReferenceEquals(null, other)) return false;

            if (ReferenceEquals(this, other)) return true;

            if (!Solution.Equals(other.Solution)) return false;

            return this.SequenceEqual(other, FloatingPointComparer.Instance);
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }
    }
}
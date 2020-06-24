using System.Linq;
using SbbChallenge.Helpers;
using sbbChallange.Layers;
using sbbChallange.ProblemDefinition;

namespace sbbChallange.IntegrityChecks
{
    public static class GraphMachinesIntegrity
    {
        public static readonly Asserts.Switch Switch = new Asserts.Switch(nameof(GraphMachinesIntegrity), 
            "conjunctive/disjunctive arcs correspond to the solution routing/job sequencing.");
        
        /// <summary>
        /// The route selection (part of the 'solution') and the order in which jobs occupy machines
        /// ('machine layer') also define a graph (implicitly). This method checks if this
        /// implicitly defined graph corresponds to the one defined in the 'graph layer'.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        public static void Check (
            Problem problem,
            ISolution solution, 
            IGraphLayer graphLayer,
            ISequencingLayer sequencingLayer)
        {
            if (!Switch.On) return;
            
            Asserts.Assert(Switch, graphLayer.Count == problem.OperationCount);
            
            IGraph newGraph = new Graph(graphLayer.Count);

            foreach (Route route in problem.Select(j => solution.GetRoute(j, canBeNull: true)).Where(r => r != null))
            {
                foreach (var (op0, op1) in route.Pairwise())
                {
                    newGraph.AddArc(op0.Id, op1.Id, -1, op0.Runtime);
                }
            }

            foreach (IJobSequence permutation in sequencingLayer)
            {
                foreach (var (mOcc0, mOcc1) in permutation.Pairwise())
                {
                    newGraph.AddArc(
                        mOcc0.LastOperation + 1,
                        mOcc1.FirstOperation,
                        mOcc0.MachineId,
                        solution.GetMachineReleaseTime(mOcc0, mOcc1));
                }
            }
            
            Asserts.Assert(Switch, newGraph.Equals(graphLayer), 
                "1 = graphLayer, 2 = implicit graph\n" + graphLayer.DifferencesToString(newGraph));
        }
    }
}
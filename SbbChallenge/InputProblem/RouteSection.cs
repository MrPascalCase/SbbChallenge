using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using SbbChallenge.Helpers;
using sbbChallange.ProblemDefinition;

namespace sbbChallange.SbbProblem
{
    
    // instantiated by json deserialization, name kept consistent with resp. json files
    // ReSharper disable ClassNeverInstantiated.Global
    // ReSharper disable UnassignedField.Global
    // ReSharper disable InconsistentNaming
    // ReSharper disable MemberCanBePrivate.Global
    public class RouteSection : IOperation
    {
        /// <summary>
        /// an ordering number. The train passes over the route_sections in this order. This is necessary because the
        /// JSON specification does not guarantee that the sequence in the file is preserved when deserializing.
        /// </summary>
        public int sequence_number;
        
        /// <summary>
        /// a label for the exit event out of this route_section. Route sections from other route_paths with the same
        /// label are "glued" together, i.e. become the same node in the route graph.
        /// </summary>
        public List<string> route_alternative_marker_at_exit;
        
        /// <summary>
        /// a label for the entry event into this route_section. Route sections from other route_paths with the same
        /// label are "glued" together, i.e. become the same node in the route graph.
        /// </summary>
        public List<string> route_alternative_marker_at_entry;
        
        /// <summary>
        /// labels that mark this route_section as a potential section to fulfil a section_requirement that has any of these as section_marker. 
        /// Note: In all our problem instances, each route_section has at most one section_marker, i.e. the list has length at most one.
        /// </summary>
        public List<string> section_marker;

        /// <summary>
        /// Non-negative. 
        /// used in the objective function for the timetable. If a train uses this route_section, this penalty accrues. 
        /// This field is optional. If it is not present, this is equivalent to penalty = 0.
        /// </summary>
        public double? penalty;
        
        /// <summary>
        /// use get_minimum_running_time instead, this is used for parsing only.
        /// </summary>
        public string minimum_running_time; // check if has to be string
        

        public List<ResourceOccupation> resource_occupations;
        
        // -------------------------------------------------------------------------------------------------------------
        //   Implementation of IOperation:
        //  ------------------------------------------------------------------------------------------------------------
        [ScriptIgnore] public TimeSpan Runtime { get; private set; }
        [ScriptIgnore] public TimeSpan EarliestEntry { get; private set; }
        [ScriptIgnore] public TimeSpan LatestEntry { get; private set; }
        [ScriptIgnore] public double DelayWeight { get; private set; }
        [ScriptIgnore] public IEnumerable<IMachine> Machines { get; private set; }
        [ScriptIgnore] public IEnumerable<IConnection> OutgoingConnections { get; private set; }
        [ScriptIgnore] public IEnumerable<IConnection> IncomingConnections => SbbIncomingConnections;

        [ScriptIgnore] internal List<Connection> SbbIncomingConnections;
        
        internal void Setup(
            ServiceIntention currentIntention,
            SectionRequirement previousSectionRequirement,
            SectionRequirement currentSectionRequirement, 
            Dictionary<string, Resource> idToResource,
            List<Connection> ontoNeedsBeSet,
            bool isLast)
        {
            Runtime = minimum_running_time == null ? TimeSpan.Zero : XmlConvert.ToTimeSpan(minimum_running_time);
            if (currentSectionRequirement != null 
                && !string.IsNullOrEmpty(currentSectionRequirement.min_stopping_time))
            {
                Runtime += XmlConvert.ToTimeSpan(currentSectionRequirement.min_stopping_time);
            }

            EarliestEntry = currentSectionRequirement?.entry_earliest ?? TimeSpan.MinValue;

            if (previousSectionRequirement?.exit_earliest != null 
                && previousSectionRequirement.exit_earliest.Value > EarliestEntry)
            {
                EarliestEntry = previousSectionRequirement.exit_earliest.Value;
            }

            LatestEntry = currentSectionRequirement?.entry_latest ?? TimeSpan.MaxValue;
            DelayWeight = currentSectionRequirement?.entry_delay_weight?? 0.0;

            if (LatestEntry == TimeSpan.MaxValue) DelayWeight = 0.0;
            
            if (previousSectionRequirement?.exit_latest != null 
                && LatestEntry > previousSectionRequirement.exit_latest.Value)
            {
                LatestEntry = previousSectionRequirement.exit_latest.Value;
                DelayWeight = previousSectionRequirement.exit_delay_weight;
            }

            Machines = resource_occupations?.Select(o => idToResource[o.resource]).ToArray() ?? new IMachine[0];


            if (currentSectionRequirement?.connections != null)
            {
                foreach (Connection connection in currentSectionRequirement.connections.Where(c => c != null))
                {
                    connection.From = currentIntention;

                    if (!ontoNeedsBeSet.Contains(connection)) ontoNeedsBeSet.Add(connection);
                }

                OutgoingConnections = currentSectionRequirement.connections.ToArray();
            }
            
            else OutgoingConnections = new Connection[0];
            
            SbbIncomingConnections = new List<Connection>();
        }

        public override string ToString()
        {
            if (Machines == null && OutgoingConnections == null)
            {
                return "Unprocessed Route Section";
            }
            
            StringBuilder builder = new StringBuilder();
            builder.Append("Section { ");
            builder.Append($"runtime={Runtime.Show()}, ");
            builder.Append($"earliest={EarliestEntry.Show()}, ");
            builder.Append($"latest={LatestEntry.Show()}, ");
            builder.Append($"weight={DelayWeight}, ");
            builder.Append($"machines={{{string.Join(", ", Machines ?? new IMachine[0])}}} ");
            builder.Append("}");
            return builder.ToString();
        }
    }
}
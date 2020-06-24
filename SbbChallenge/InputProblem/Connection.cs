using System;
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
    
    /// <summary>  Owned by a section requirement, <see cref="SectionRequirement"/>  </summary>
    public class Connection : ProblemDefinition.IConnection
    {
        /// <summary> technical id. Irrelevant during processing  </summary>
        public string id;  
        
        /// <summary> reference to the service_intention that accepts the connection  </summary>
        public string onto_service_intention;
        
        /// <summary> reference to a section marker. Specifies which route_sections in the onto_service_intention are candidates
        /// to fulfil the connection  </summary>
        public string onto_section_marker;
        
        /// <summary> used for parsing only: use get_min_connection_time instead  </summary>
        public string min_connection_time;

        /// <summary> minimum duration required between arrival and departure event.  </summary>
        public TimeSpan GetMinConnectionTime => XmlConvert.ToTimeSpan(min_connection_time);

        TimeSpan IConnection.Length => XmlConvert.ToTimeSpan(min_connection_time);

        [ScriptIgnore] public ServiceIntention From;
        [ScriptIgnore] public ServiceIntention Onto;
        
        public bool Equals(IConnection other)
        {
            return ReferenceEquals(this, other);
        }

        public override string ToString()
        {
            return $"Connection to: {onto_service_intention}#{onto_section_marker} ({GetMinConnectionTime.Show()})";
        }
    }
}
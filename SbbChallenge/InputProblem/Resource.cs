using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using sbbChallange.ProblemDefinition;

namespace sbbChallange.SbbProblem
{
    // instantiated by json deserialization, name kept consistent with resp. json files
    // ReSharper disable ClassNeverInstantiated.Global
    // ReSharper disable UnassignedField.Global
    // ReSharper disable InconsistentNaming
    // ReSharper disable MemberCanBePrivate.Global
    public class SectionRequirement
    {
        public long sequence_number;
        
        public string section_marker;
        
        public string min_stopping_time;

        public TimeSpan? entry_earliest; 
        public TimeSpan? entry_latest; 
        public TimeSpan? exit_earliest;
        public TimeSpan? exit_latest; 
        
        /// <summary> used to calculate total delay penalties in the objective function </summary>
        public double entry_delay_weight; // non-negative
        
        /// <summary> used to calculate total delay penalties in the objective function </summary>
        public double exit_delay_weight;    // non-negative
        
        public List<Connection> connections;

        public override string ToString()
        {
            return ((entry_earliest != null? $", entry earliest {entry_earliest}" : "")
                    + (entry_latest != null? $", entry latest {entry_latest}" : "")
                    + (exit_earliest != null? $", exit earliest {exit_earliest}" : "")
                    + (exit_latest != null? $", exit latest {exit_latest}" : "")).Substring(2);
        }

    }
    
    
    public class ResourceOccupation
    {
        /// <summary>  a reference to the id of the resource that is occupied  </summary>
        public string resource;

        [ScriptIgnore] public Resource ref_to_resource;
        
        /// <summary>
        /// a description of the direction in which the resource is occupied. This field is only relevant for resources
        /// that allow "following" trains, which does not occur in the problem instances for this chalenge.
        /// You may ignore this field.
        /// </summary>
        public string occupation_direction;


        public override string ToString()
        {
            return $"Occupation: ref {ref_to_resource.ToString()}";
        }
    }
    
    public class Resource : IMachine
    {
        /// <summary>  unique identifier for the resource. This is referenced in the resource_occupations
        /// </summary>
        public string id;

        /// <summary>  describes how much time must pass between release of a resource by one train and the following
        /// occupation  by the next train. During ProblemInstance setup this value may change, as, when resources are
        /// merged,  we have to assign the max release time to the remaining resource.  </summary>
        public string release_time;  
        
        /// <summary>  flag whether the resource is of following type (true) or of blocking type (false). 
        /// As mentioned, all resources in all the provided problem instances have this field set to false
        /// </summary>
        public bool following_allowed;                   
        
        public override string ToString() => id;
        

        // -------------------------------------------------------------------------------------------------------------
        //   Implementation of IMachine:
        //  ------------------------------------------------------------------------------------------------------------
        TimeSpan IMachine.ReleaseTime => System.Xml.XmlConvert.ToTimeSpan(release_time);

        protected bool Equals(Resource other)
        {
            return string.Equals(id, other.id);
        }

        public bool Equals(IMachine other)
        {
            return other is Resource res && Equals(res);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Resource) obj);
        }
        
        // id cannot be made readonly because of the json deserialization
        // ReSharper disable NonReadonlyMemberInGetHashCode
        public override int GetHashCode()
        {
            return (id != null ? id.GetHashCode() : 0);
        }
    }

}
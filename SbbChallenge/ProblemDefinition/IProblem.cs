using System;
using System.Collections.Generic;

namespace sbbChallange.ProblemDefinition
{
    public enum ObjectiveType { MaximumWeighedTardiness, SumWeightedTardiness }    
    
    public interface IProblem
    {
        string ProblemName { get; }
        ObjectiveType Objective { get; }
        IEnumerable<IJob> Jobs { get; }
    }

    public interface IJob { IEnumerable<IRoute> Routes { get; } }

    public interface IRoute
    {
        IEnumerable<IOperation> Operations { get; }
        double RoutingPenalty { get; }
    }

    public interface IOperation
    {
        TimeSpan Runtime { get; }
        TimeSpan EarliestEntry { get; }
        TimeSpan LatestEntry { get; }
        double DelayWeight { get; }
        IEnumerable<IMachine> Machines { get; }
        IEnumerable<IConnection> OutgoingConnections { get; }
        IEnumerable<IConnection> IncomingConnections { get; }
    }

    public interface IMachine : IEquatable<IMachine> { TimeSpan ReleaseTime { get; } }

    public interface IConnection : IEquatable<IConnection> { TimeSpan Length { get; } }
}
# Adaptation of the Gröflin-Klinkert-Bürgy Local Search for the Sbb Crowd AI Challenge

On this github repository you find my master [thesis](https://github.com/MrPascalCase/SbbChallenge/blob/master/Thesis.pdf) & project. 

The SBB (swiss federal railways) challenge is a time table generation problem. At the core this is a *blocking-job-shop scheduling problem*: Given is a set of *jobs* (trains) each has to go through a set of *operations*. Operations conflict if they use the same *machine* (track segment). Each machine can only be used by one job at a time. The aim is, to assign conflict free entry times, such that the delay is minimized. A main complication comes form the *blocking* nature of the problem: a machine is only freed, when a job moved to the next.

The links to the challenge:

* <https://www.crowdai.org/challenges/train-schedule-optimisation-challenge>

* <https://github.com/crowdAI/train-schedule-optimisation-challenge-starter-kit>



## Dependencies

* .Net Framework v4.7.2 (or later), which includes

    - mscorlib, System
    - System.Web.Extension (only used for the SBB json input parsing)
    - System.Xml (only used to convert SBB time stamps)
    - System.Drawing (only used for color names to generate debug svg output)

* dot / graphviz if you want to output debug visualizations. (<https://graphviz.org/>)

## Usage

```console
$ msbuild SbbChallenge.csproj
$ mono obj/SbbChallenge.exe -i <path-to-sbb-problem>
```

## Documentation

The code is mostly documented though interfaces and comments. Here by an quick overview of the project organization. I presume that the reader has some knowledge of the algorithm implemented here. For this I refer to the thesis (pdf). Chapters 1 & 4 can be skipped. Chapter 2 can be skipped by readers familiar with the work of Gröflin, Klinkert & Bürgy. 

First, we get the easy things out of the way. These name-spaces ....

* **IntegrityChecks:** fancy asserts. There are many, and some are so slow, that it became necessary to enable/disable and document them. 

* **Helper:** some LINQ-like extensions and basic data types. Of note is the class Graph which implements a forward/backward adjacency list graph (vertices : int, edges labeled with (machineId : int) * (length : TimeSpan)). 

* **ProblemDefintion:** IProblem defines the blocking-job-shop problem general enough, that both, instances studied in academia and the problems provided by SBB are modelled as such. The class Problem (immutable) is constructed form an IProblem and caches/preprocesses various things. ProblemTransformation provided IProblem extension methods to remove unnecessary Routes/Machines/Operations.

* **InputProblem:** Constructs the SBB (or academic literature) blocking-job-shop problem from json (or txt) files. Implements ProblemDefinition.IProblem.

* **Visualization:** Can create svg debug output for the precedence-constraint graphs we are working with.

* **Search:** The algorithm provides a neighborhood. This runs a taboo-search to find a local optimum. It is kept as simple as possible. 

The core of the algorithm, the names-space **Layers:** I tried to split the algorithm into reasonable modules to increase readability and to make modifications/experiments easier. Guiding principles are *single responsibility* and avoidance of *cyclic dependencies*. At the bottom of the resulting layered approach we have a *ISolution* (representing the vector *t* in the thesis). Built on-top we have layers which expose ever more complex modifications of the ISolution. While the modifications increase in complexity, they come closer to maintaining the feasibility of solutions. Note, the file Layers._Interfaces.cs, which both enforces some modularity and provides some overview/documentation is a good starting point to read the code.

<img src="https://github.com/MrPascalCase/SbbChallenge/blob/master/ReadmeImage.png" alt="drawing" height="600"/>

## Most important references

[Gröflin H, Klinkert A (2007) Feasible insertions in job shop scheduling, short cycles and stable sets](https://www.sciencedirect.com/science/article/pii/S0377221706000063)

[Gröflin H, Klinkert A (2009) A new neighborhood and tabu search for the Blocking Job Shop](https://www.sciencedirect.com/science/article/pii/S0166218X09000870)

[Bürgy R (2017) A neighborhood for complex job shop scheduling problems with regular objectives](https://link.springer.com/article/10.1007/s10951-017-0532-2)

[Bürgy R, Gröflin H (2017) The no-wait job shop with regular objective: a method based on optimal job insertion](https://link.springer.com/article/10.1007/s10878-016-0020-1)

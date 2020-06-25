# Adaptation of the Gröflin-Klinkert-Bürgy Local Search for the Sbb Crowd AI Challenge

On this github repositry you find the code for my master thesis & project. 

The SBB (swiss federal railways) challenge is a time table generation probelm. At the core this corresponds to academic model of a *blocking-job-shop scheduling problem*: Given is a set of *jobs* (trains) each has to go through a set of *operations*. Operations conflict if they use the same *machine* (track segment). Each machine can only be used by one job at a time. The aim is, to assign conflict free entry times, such that the delay is minimized. A main complication comes form the *blocking* nature of the problem: a machine is only freed, when a job moved to the next.

The links to the challenge:

* <https://www.crowdai.org/challenges/train-schedule-optimisation-challenge>

* <https://github.com/crowdAI/train-schedule-optimisation-challenge-starter-kit>



## Depenacies

* .Net Framework v4.7.2 (or later), which includes

    - mscorlib, System
    - System.Web.Extension (only used for the SBB json input parsing)
    - System.Xml (only used to convert SBB time stamps)
    - System.Drawing (only used for color names to generate debug svg output)

* dot / graphviz if you want to output debug visualizations. (<https://graphviz.org/>)

## Usage

```console
$ msbuild sbbChallenge.csproj
$ mono obj/sbbChallenge.exe -i <path-to-sbb-problem>
```

## Documentation

The code is mostly documented though the use of interfaces and comments. This I complement here by an quick overview of the project organization. I presume that the reader has some knowledge of the algorithm implemented here. For this I refer to the thesis (pdf). Chapters 1 & 4 can be skiped. Chapter 2 can be skiped by readers familiar with the work of Gröflin, Klinkert & Bürgy. 

First, we get the easy things out of the way. These namespaces ....

* **IntegrityChecks:** essentially glorified asserts. Asserts are categorized, as some of them are so slow that I need control over which ones are currently on/checked and which ones are off. 

* **Helper:** some LINQ-like extensions and basic data types. Of note is the class Graph which implements a forward/backward adjacency list graph (vertices : int, edges labeled with (machineId : int) * (lenght : TimeSpan)). 

* **ProblemDefintion:** IProblem defines the blocking-job-shop problem general enough, that both, instances studied in academia and the problems provided by SBB are modelled as such. The class Problem (immutable) is constructed form an IProblem and caches/preprocesses various things. (Problem also implements IProblem, so that you could write IProblem transformers that depend on cached/prepocessed properties) ProblemTransformation provided IProblem extension methods to remove unnecessary Routes/Machines/Operations.

* **InputProblem:** Consturcts the SBB (or academic literature) blocking-job-shop problem from json (or txt) files. Implements ProblemDefinition.IProblem.

* **Visualization:** Can create svg debug output for precedence-constraint graph we are working with.

* **Search:** The algorithm provides a neighbourhood. This runs a taboo-search to find a local optimum. It is keept as simple as possible. 

With this out of the way, we come to the core of the algorithm, the namesspace **Layers**. I tried to split the algorithm into reasonable modules to increase readability and make modifications/experiments easier. Guideing princibles are *single responsability* and avoidance of *cyclic dependancies*. At the bottom of the layered approach we have a *ISolution* (representing the vector *t* in the thesis). Built on-top we have layers which expose ever more complex modifiations of the ISolution. While the modifications increase in complexity, they come closer to maintaining the feasibilty of solutions. 

<img src="https://github.com/MrPascalCase/SbbChallenge/blob/master/readme_img_layers.png" alt="drawing" height="600"/>

## Where to start?

Make sure you understand the problem definition in ProblemDefinition.IProblem, ie. the blocking-jop-shop scheduling problem first. Second, Layers._Interfaces.cs is intended to serve the dual purpose of enforcing modularity and providing some overview/documentation.


## Most important references

[Gröflin H, Klinkert A (2007) Feasible insertions in job shop scheduling, short cycles and stable sets](https://www.sciencedirect.com/science/article/pii/S0377221706000063)

[Gröflin H, Klinkert A (2009) A new neighborhood and tabu search for the Blocking Job Shop](https://www.sciencedirect.com/science/article/pii/S0166218X09000870)

[Bürgy R (2017) A neighborhood for complex job shop scheduling problems with regular objectives](https://link.springer.com/article/10.1007/s10951-017-0532-2)

[Bürgy R, Gröflin H (2017) The no-wait job shop with regular objective: a method based on optimal job insertion](https://link.springer.com/article/10.1007/s10878-016-0020-1)

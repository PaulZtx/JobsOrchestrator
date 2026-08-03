// See https://aka.ms/new-console-template for more information

using Jobs;
using Jobs.JobsEntities;

var orchestrator = new JobsOrchestrator();
var job = new SampleJob();
orchestrator.TryAddJob(job);

Console.ReadKey();

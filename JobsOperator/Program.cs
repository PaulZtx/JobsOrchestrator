// Пример динамической загрузки и запуска задания

using Jobs;
using Jobs.JobsEntities.Interfaces;
using TestBuilds;


// var path = "C:\\Users\\stsma\\RiderProjects\\JobsOperator\\TestBuilds\\bin\\Debug\\net10.0\\TestBuilds.dll";
// var path = Path.Combine("..", "..", "..", "..", "TestBuilds", "bin", "Debug", "net10.0", "TestBuilds.dll");
// Assembly MyAssembly = Assembly.LoadFrom(path);
//
// var type = MyAssembly.ExportedTypes.FirstOrDefault(_ => _.FullName.EndsWith("job", StringComparison.InvariantCultureIgnoreCase));
// // Создание задания по полному имени типа
// IJob job = (IJob)MyAssembly.CreateInstance(type.FullName);

var jobMode = Environment.GetEnvironmentVariable("JOB_MODE");
IJob job = string.Equals(jobMode, "kafka-relay", StringComparison.OrdinalIgnoreCase)
    ? new KafkaRelayJob()
    : new SampleJob();

var orchestrator = new JobsOrchestrator();
var status = orchestrator.TryAddJob(job);
if (status.JobId is null)
    throw new InvalidOperationException(status.ErrorMessage ?? "Could not start job.");

Console.WriteLine($"Job mode '{jobMode ?? "sample"}' is running.");

if (job is KafkaRelayJob)
{
    await Task.Delay(Timeout.InfiniteTimeSpan);
}
else
{
    Console.ReadKey();
}

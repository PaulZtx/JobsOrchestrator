// Пример динамической загрузки и запуска задания

using System.Reflection;
using Jobs;
using Jobs.JobsEntities;
using Jobs.JobsEntities.Interfaces;


var path = "C:\\Users\\stsma\\RiderProjects\\JobsOperator\\TestBuilds\\bin\\Debug\\net10.0\\TestBuilds.dll";

Assembly MyAssembly = Assembly.LoadFrom(path);

var type = MyAssembly.ExportedTypes.FirstOrDefault(_ => _.FullName.EndsWith("job", StringComparison.InvariantCultureIgnoreCase));
// Создание задания по полному имени типа
IJob job = (IJob)MyAssembly.CreateInstance(type.FullName);

var orchestrator = new JobsOrchestrator();
orchestrator.TryAddJob(job);

Console.ReadKey();

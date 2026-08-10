using System.Text;
using System.Text.Json;
using Jobs.Connectors;
using Jobs.Sources.Interfaces;

namespace Jobs.JobsEntities;

public class CheckpointCoordinator
{
    private readonly CheckpointCoordinatorOptions checkpointCoordinatorOptions;
    
    public CheckpointCoordinator(CheckpointCoordinatorOptions options, IReadOnlyList<ISourceRunner> sourceRunners)
    {
        checkpointCoordinatorOptions = options;
        CaptureCheckPointEvent += async () =>
        {
            try
            {
                await Task.WhenAll(sourceRunners.Select(_ => _.PauseAsync()));

                (string Name, SourcePosition PositionSource)[] positions = await Task.WhenAll(
                    sourceRunners.Select(async _ =>
                    {
                        var res = await _.CaptureStateAsync();
                        return (_.Name, res);
                    }));

                var sourcePositions = positions.ToDictionary(valueTuple => valueTuple.Name,
                    valueTuple => valueTuple.PositionSource);

                var data = JsonSerializer.Serialize(sourcePositions);
                var bytes = Encoding.UTF8.GetBytes(data);

                await using var stream = File.Create(options.JobStartOptions.CheckpointPath);
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }
            finally
            {
                await Task.WhenAll(sourceRunners.Select(_ => _.ResumeAsync()));
            }
        };
    }
    
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if(checkpointCoordinatorOptions.CheckpointOptions is null || !checkpointCoordinatorOptions.CheckpointOptions.Enabled)
            return;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(checkpointCoordinatorOptions.CheckpointOptions.DelayMillisecond, cancellationToken);

            await CaptureCheckPointEvent.Invoke();
        }
    }
    
    public delegate Task CaptureCheckPoint();
    
    public event CaptureCheckPoint CaptureCheckPointEvent;
}

public class CheckpointOptions
{
    public bool Enabled { get; set; }
    
    public int DelayMillisecond { get; set; }
}

public class CheckpointCoordinatorOptions
{
    public JobStartOptions JobStartOptions { get; set; }
    
    public CheckpointOptions CheckpointOptions { get; set; }
}
using Jobs.Enums;

namespace Jobs.JobsEntities;

public sealed class JobStartOptions
{
    public string? CheckpointPath { get; init; }

    public CheckpointRestoreMode RestoreMode { get; init; }
        = CheckpointRestoreMode.ResumeOrCreate;
}
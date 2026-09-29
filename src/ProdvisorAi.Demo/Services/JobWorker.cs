using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;

namespace ProdvisorAi.Demo.Services;

/// <summary>
/// Background service that processes generation jobs one at a time:
/// pending -> running -> done | failed. The UI polls GET /api/jobs/{id} for the status.
/// </summary>
public sealed class JobWorker(JobQueue queue, IServiceScopeFactory scopes, ILogger<JobWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (var jobId in queue.ReadAllAsync(stoppingToken))
        {
            try { await ProcessAsync(jobId, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Unexpected error while processing job {JobId}", jobId); }
        }
    }

    async Task ProcessAsync(string jobId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<TechnologyGenerator>();

        var job = await db.Jobs.FindAsync([jobId], ct);
        if (job is null || job.Status is JobStatus.Done or JobStatus.Failed) return;

        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var sw = Stopwatch.StartNew();
        try
        {
            await generator.GenerateAsync(job, ct);
            job.Status = JobStatus.Done;
        }
        catch (AiServiceException ex)
        {
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
            job.ErrorKind = ex.Kind;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(ex, "Job {JobId} failed with an unexpected error", jobId);
            job.Status = JobStatus.Failed;
            job.Error = $"Unexpected error: {ex.Message}";
            job.ErrorKind = "internal";
        }

        sw.Stop();
        job.CompletedAt = DateTime.UtcNow;
        job.DurationMs = sw.ElapsedMilliseconds;
        await db.SaveChangesAsync(CancellationToken.None);

        // One structured log line per job. Never includes the API key or the drawing.
        log.LogInformation(
            "Job {JobId} {Status}: model={Model} mock={Mock} durationMs={DurationMs} tokens in/out/total={InputTokens}/{OutputTokens}/{TotalTokens} part={PartNumber} errorKind={ErrorKind}",
            job.Id, job.Status, job.Model, job.Mock, job.DurationMs, job.InputTokens, job.OutputTokens, job.TotalTokens, job.PartNumber, job.ErrorKind);
    }

    /// <summary>Jobs that were queued or running when the app stopped are picked up again.</summary>
    async Task RequeueUnfinishedAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await db.Jobs
            .Where(j => j.Status == JobStatus.Pending || j.Status == JobStatus.Running)
            .OrderBy(j => j.CreatedAt).Select(j => j.Id).ToListAsync(ct);

        foreach (var id in ids) await queue.EnqueueAsync(id, ct);
        if (ids.Count > 0) log.LogInformation("Re-queued {Count} unfinished job(s) after restart", ids.Count);
    }
}

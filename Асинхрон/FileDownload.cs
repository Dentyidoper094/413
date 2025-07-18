using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class FileDownloader : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly HttpClient _httpClient;
    private readonly long _maxBytesPerSecond;
    private readonly ConcurrentDictionary<string, Stream> _activeDownloads;
    private readonly IProgress<DownloadProgress> _progress;
    private readonly CancellationTokenSource _globalCts;

    public FileDownloader(int maxConcurrentDownloads, long maxBytesPerSecond, IProgress<DownloadProgress> progress)
    {
        _semaphore = new SemaphoreSlim(maxConcurrentDownloads, maxConcurrentDownloads);
        _httpClient = new HttpClient();
        _maxBytesPerSecond = maxBytesPerSecond;
        _activeDownloads = new ConcurrentDictionary<string, Stream>();
        _progress = progress;
        _globalCts = new CancellationTokenSource();
    }

    public async Task DownloadFilesAsync(IEnumerable<DownloadTask> downloadTasks, CancellationToken externalToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, externalToken);
        var token = linkedCts.Token;

        var tasks = downloadTasks.Select(task => DownloadFileAsync(task, token)).ToList();
        await Task.WhenAll(tasks);
    }

    private async Task DownloadFileAsync(DownloadTask downloadTask, CancellationToken token)
    {
        try
        {
            await _semaphore.WaitAsync(token);

            var report = new DownloadProgress
            {
                FileName = downloadTask.FileName,
                Status = DownloadStatus.Starting,
                ActiveDownloads = _semaphore.CurrentCount
            };
            _progress.Report(report);
            EnsureDiskSpace(downloadTask.DestinationPath);

            using var response = await _httpClient.GetAsync(downloadTask.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var buffer = new byte[8192];
            long totalRead = 0;
            var lastUpdateTime = DateTime.Now;
            long bytesSinceLastUpdate = 0;

            await using var contentStream = await response.Content.ReadAsStreamAsync(token);
            await using var fileStream = new FileStream(downloadTask.DestinationPath, FileMode.Create, FileAccess.Write);

            _activeDownloads.TryAdd(downloadTask.Url, contentStream);

            report.Status = DownloadStatus.Downloading;
            report.TotalBytes = totalBytes;
            _progress.Report(report);

            while (true)
            {
                token.ThrowIfCancellationRequested();

                var readTask = contentStream.ReadAsync(buffer, 0, buffer.Length, token);
                var bytesRead = await readTask;

                if (bytesRead == 0) break;

                await fileStream.WriteAsync(buffer, 0, bytesRead, token);

                totalRead += bytesRead;
                bytesSinceLastUpdate += bytesRead;


                await ThrottleIfNeeded(bytesSinceLastUpdate, ref lastUpdateTime, ref bytesSinceLastUpdate, token);

                report.BytesDownloaded = totalRead;
                _progress.Report(report);
            }

            report.Status = DownloadStatus.Completed;
            _progress.Report(report);
        }
        catch (OperationCanceledException)
        {
            var report = new DownloadProgress
            {
                FileName = downloadTask.FileName,
                Status = DownloadStatus.Canceled,
                ActiveDownloads = _semaphore.CurrentCount
            };
            _progress.Report(report);
            throw;
        }
        catch (Exception ex)
        {
            var report = new DownloadProgress
            {
                FileName = downloadTask.FileName,
                Status = DownloadStatus.Failed,
                ErrorMessage = ex.Message,
                ActiveDownloads = _semaphore.CurrentCount
            };
            _progress.Report(report);
        }
        finally
        {
            _semaphore.Release();
            _activeDownloads.TryRemove(downloadTask.Url, out _);
        }
    }

    private async Task ThrottleIfNeeded(long bytesSinceLastUpdate, ref DateTime lastUpdateTime, ref long bytesSinceLastUpdateReset, CancellationToken token)
    {
        if (_maxBytesPerSecond <= 0) return;

        var elapsed = DateTime.Now - lastUpdateTime;
        if (elapsed.TotalMilliseconds >= 1000)
        {
            lastUpdateTime = DateTime.Now;
            bytesSinceLastUpdateReset = 0;
            return;
        }

        var targetTime = bytesSinceLastUpdate / (double)_maxBytesPerSecond * 1000;
        var remainingTime = targetTime - elapsed.TotalMilliseconds;

        if (remainingTime > 0)
        {
            await Task.Delay((int)remainingTime, token);
            lastUpdateTime = DateTime.Now;
            bytesSinceLastUpdateReset = 0;
        }
    }

    private void EnsureDiskSpace(string filePath)
    {
        var drive = new DriveInfo(Path.GetPathRoot(filePath));
        if (drive.AvailableFreeSpace <= 100 * 1024 * 1024) // 100MB минимальный запас
        {
            throw new IOException("Недостаточно места на диске");
        }
    }

    public void CancelAll()
    {
        _globalCts.Cancel();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _semaphore.Dispose();
        _globalCts.Dispose();
    }
}

public class DownloadTask
{
    public string Url { get; set; }
    public string FileName { get; set; }
    public string DestinationPath { get; set; }
}

public class DownloadProgress
{
    public string FileName { get; set; }
    public DownloadStatus Status { get; set; }
    public long BytesDownloaded { get; set; }
    public long? TotalBytes { get; set; }
    public int ActiveDownloads { get; set; }
    public string ErrorMessage { get; set; }
}

public enum DownloadStatus
{
    Starting,
    Downloading,
    Completed,
    Failed,
    Canceled
}
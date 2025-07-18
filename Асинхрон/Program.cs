using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static FileDownloader downloader;
    private static Dictionary<string, (int line, long lastBytes)> progressLines = new Dictionary<string, (int, long)>();
    private static int nextLine = 0;

    static async Task Main(string[] args)
    {
        Console.WriteLine("Асинхронный загрузчик файлов");
        Console.WriteLine("----------------------------\n");

     
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nОтмена операций...");
            downloader?.CancelAll();
        };

        try
        {
            
            var downloadTasks = GetDownloadTasks();


            var progress = new Progress<DownloadProgress>(ReportProgress);
            using (downloader = new FileDownloader(3, 100_000, progress))
            {
 
                Console.WriteLine("\nНачало загрузки...\n");
                await downloader.DownloadFilesAsync(downloadTasks, CancellationToken.None);
            }

            Console.WriteLine("\nВсе загрузки завершены.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nЗагрузки были отменены пользователем.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nКритическая ошибка: {ex.Message}");
        }

        Console.WriteLine("\nНажмите любую клавишу для выхода...");
        Console.ReadKey();
    }

    private static List<DownloadTask> GetDownloadTasks()
    {
        return new List<DownloadTask>
        {
            new DownloadTask
            {
                Url = "https://example.com/file1.zip",
                FileName = "file1.zip",
                DestinationPath = "file1.zip"
            },
            new DownloadTask
            {
                Url = "https://example.com/file2.zip",
                FileName = "file2.zip",
                DestinationPath = "file2.zip"
            },
            new DownloadTask
            {
                Url = "https://example.com/file3.zip",
                FileName = "file3.zip",
                DestinationPath = "file3.zip"
            },
            new DownloadTask
            {
                Url = "https://example.com/file4.zip",
                FileName = "file4.zip",
                DestinationPath = "file4.zip"
            },
    
            new DownloadTask
            {
                Url = "https://example.com/nonexistent_file.zip",
                FileName = "nonexistent_file.zip",
                DestinationPath = "nonexistent_file.zip"
            }
        };
    }

    private static void ReportProgress(DownloadProgress progress)
    {

        if (!progressLines.ContainsKey(progress.FileName))
        {
            progressLines[progress.FileName] = (nextLine++, 0);
            Console.WriteLine(); // Добавляем новую строку
        }

        var (line, _) = progressLines[progress.FileName];
        Console.SetCursorPosition(0, line);

        string message = progress.Status switch
        {
            DownloadStatus.Starting => $"{progress.FileName}: Загрузка начата...",
            DownloadStatus.Downloading when progress.TotalBytes.HasValue => 
                $"{progress.FileName}: {progress.BytesDownloaded / 1024} KB / {progress.TotalBytes / 1024} KB " +
                $"({(double)progress.BytesDownloaded / progress.TotalBytes * 100:0.0}%)",
            DownloadStatus.Downloading => $"{progress.FileName}: {progress.BytesDownloaded / 1024} KB загружено",
            DownloadStatus.Completed => $"{progress.FileName}: [✓] Завершено ({(progress.TotalBytes / 1024):N0} KB)",
            DownloadStatus.Failed => $"{progress.FileName}: [×] Ошибка - {progress.ErrorMessage}",
            DownloadStatus.Canceled => $"{progress.FileName}: [↻] Отменено пользователем",
            _ => $"{progress.FileName}: Неизвестный статус"
        };

        Console.Write(message.PadRight(Console.WindowWidth - 1));

        Console.SetCursorPosition(0, nextLine);
        Console.Write($"Активных загрузок: {progress.ActiveDownloads}".PadRight(Console.WindowWidth - 1));
    }
}
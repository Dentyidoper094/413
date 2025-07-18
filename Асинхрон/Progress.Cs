using System;
public class Progress
{
public class DownloadProgress
{
    public string FileUrl { get; set; }      
    public long BytesDownloaded { get; set; }  
    public long? TotalBytes { get; set; }      
    public int ActiveDownloads { get; set; }   
    public DownloadProgress Status { get; set; } 
    public string ErrorMessage { get; set; }   
}

}
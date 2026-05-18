namespace NzbWebDAV.Exceptions;

public class NoFilesProducedException(string message) : NonRetryableDownloadException(message)
{
}

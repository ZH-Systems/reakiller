namespace Transflux.Core;

public enum QueueState { Pending, Running, Completed, Failed, Cancelled, Interrupted }
public sealed record QueueJob(Guid Id, string[] Arguments, DateTimeOffset Created, QueueState State,
    int Attempts = 0, DateTimeOffset? Updated = null)
{
    public string Operation => Arguments[0];
    public QueueJob Recover() => State == QueueState.Running
        ? this with { State = QueueState.Interrupted, Updated = DateTimeOffset.UtcNow } : this;
    public QueueJob Retry()
    {
        if (State is not (QueueState.Failed or QueueState.Cancelled or QueueState.Interrupted))
            throw new InvalidOperationException("Only failed or interrupted jobs can be retried.");
        // Remove overwrite switches, not option values which happen to contain that text.
        var args = Arguments.Take(2).ToList();
        for (int i = 2; i < Arguments.Length; i++)
        {
            var flag = Arguments[i];
            if (flag == "--overwrite") continue;
            args.Add(flag);
            if (flag is not ("--recursive" or "--keep-headers")) args.Add(Arguments[++i]);
        }
        return this with { Arguments = args.ToArray(), State = QueueState.Pending, Updated = DateTimeOffset.UtcNow };
    }

    public void Validate()
    {
        if (Id == Guid.Empty || Arguments is null || Arguments.Length < 4 || Arguments.Length > 512 ||
            Arguments.Any(a => a is null || a.Length > 4096 || a.Contains('\0')) ||
            Arguments[0] is not ("xml-to-csv" or "pdf-to-csv") || !Path.IsPathFullyQualified(Arguments[1]) ||
            !Enum.IsDefined(State) || Attempts < 0) throw new InvalidDataException("Invalid queue job.");
        var output = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 2; i < Arguments.Length; i++)
        {
            var flag = Arguments[i];
            if (flag is not ("--column" or "--header" or "--boundary") && !seen.Add(flag))
                throw new InvalidDataException("Duplicate queue option.");
            if (flag is "--recursive" or "--overwrite") continue;
            if (Arguments[0] == "pdf-to-csv" && flag == "--keep-headers") continue;
            if (!(flag is "--output" or "--threads" ||
                (Arguments[0] == "xml-to-csv" && flag is "--record-path" or "--column") ||
                (Arguments[0] == "pdf-to-csv" && flag is "--first-page" or "--last-page" or "--boundary" or "--header" or "--ocr-config")))
                throw new InvalidDataException("Unsupported queue option.");
            if (++i >= Arguments.Length || string.IsNullOrWhiteSpace(Arguments[i]))
                throw new InvalidDataException("Missing queue option value.");
            if (flag is "--output" or "--ocr-config" && !Path.IsPathFullyQualified(Arguments[i]))
                throw new InvalidDataException("Queue paths must be absolute.");
            if (flag == "--output") output = true;
        }
        if (!output) throw new InvalidDataException("Queue output required.");
    }
}

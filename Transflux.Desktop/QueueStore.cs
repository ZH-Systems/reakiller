using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Transflux.Core;

namespace Transflux.Desktop;

internal sealed class QueueStore : IDisposable
{
    private const int Limit = 16 * 1024 * 1024;
    private readonly string path;
    private readonly FileStream lease;
    internal QueueStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZH Systems", "Transflux", "Queue");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "jobs.dat");
        lease = new FileStream(Path.Combine(directory, "queue.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    internal List<QueueJob> Load()
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > Limit) throw new InvalidDataException("Queue exceeds storage limit.");
        var plain = Transform(File.ReadAllBytes(path), false);
        try
        {
            var jobs = JsonSerializer.Deserialize<List<QueueJob>>(plain) ?? throw new InvalidDataException("Invalid queue.");
            if (jobs.Count > 500 || jobs.Select(j => j.Id).Distinct().Count() != jobs.Count) throw new InvalidDataException("Invalid queue entries.");
            foreach (var job in jobs) job.Validate();
            return jobs.Select(j => j.Recover()).ToList();
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    internal void Save(IEnumerable<QueueJob> jobs)
    {
        var list = jobs.ToList();
        if (list.Count > 500) throw new InvalidDataException("Queue is full.");
        foreach (var job in list) job.Validate();
        var plain = JsonSerializer.SerializeToUtf8Bytes(list);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (plain.Length > Limit / 2) throw new InvalidDataException("Queue exceeds storage limit.");
            var encrypted = Transform(plain, true);
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(encrypted); file.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); File.Delete(temp); }
    }
    public void Dispose() => lease.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = encrypt
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok || output.Size < 0 || output.Size > Limit) throw new CryptographicException("Queue protection failed.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            for (int i = 0; i < input.Size; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                if (output.Size > 0 && output.Size <= Limit)
                    for (int i = 0; i < output.Size; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }
}

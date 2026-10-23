using PersonalVault.Storage;
using PersonalVault.Models;

// Usage: comparevaults.exe "Label 1=C:\path\to\snapshot1.pvlt" "Label 2=C:\path\to\snapshot2.pvlt" [...]
//
// Compares two or more vault snapshots (.pvlt files - the live vault, a dated local
// backup, or a downloaded Drive revision all work) and reports what was added, removed,
// or modified between each consecutive pair. Built to investigate "did I lose data"
// questions by diffing point-in-time snapshots instead of guessing from file sizes.
//
// Safety: this only reads local files and writes a SAFE summary (account names,
// categories, institutions, created/modified dates) to comparison-result.txt next to
// the exe. It never prints or saves usernames, passwords, account numbers, notes, or
// any other sensitive field, and the master secret is typed directly into this console
// window - nothing else in the app or any automation ever sees it.

Console.WriteLine("=== Personal Vault - compare snapshots (local only, nothing sent anywhere) ===");
Console.WriteLine();

if (args.Length < 2)
{
    Console.WriteLine("Usage: comparevaults.exe \"Label 1=C:\\path\\snapshot1.pvlt\" \"Label 2=C:\\path\\snapshot2.pvlt\" [...]");
    Console.WriteLine("Needs at least two snapshots to compare. Press Enter to close.");
    Console.ReadLine();
    return;
}

var snapshots = new List<(string Label, string Path)>();
foreach (var arg in args)
{
    int eq = arg.IndexOf('=');
    if (eq < 0)
    {
        Console.WriteLine($"Skipping argument with no '=': {arg}");
        continue;
    }
    snapshots.Add((arg[..eq], arg[(eq + 1)..]));
}

Console.Write("Enter the master secret: ");
string secret = ReadMasked();
Console.WriteLine();

var loaded = new List<(string Label, VaultData Data)>();
try
{
    foreach (var (label, path) in snapshots)
        loaded.Add((label, VaultStorage.LoadFromBytes(File.ReadAllBytes(path), secret)));
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("Could not decrypt one or more files with that secret: " + ex.Message);
    Console.WriteLine("Press Enter to close.");
    Console.ReadLine();
    return;
}

static string Local(DateTime utc) => $"{TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local):yyyy-MM-dd HH:mm} local";

var lines = new List<string> { "Personal Vault - snapshot comparison", "" };
foreach (var (label, data) in loaded)
    lines.Add($"  {label}: {data.Accounts.Count} account(s)");
lines.Add("");

for (int i = 1; i < loaded.Count; i++)
{
    var (fromLabel, from) = loaded[i - 1];
    var (toLabel, to) = loaded[i];
    var fromById = from.Accounts.ToDictionary(a => a.Id);
    var toById = to.Accounts.ToDictionary(a => a.Id);
    var added = to.Accounts.Where(a => !fromById.ContainsKey(a.Id)).OrderBy(a => a.CreatedUtc).ToList();
    var removed = from.Accounts.Where(a => !toById.ContainsKey(a.Id)).OrderBy(a => a.CreatedUtc).ToList();
    var modified = to.Accounts
        .Where(a => fromById.TryGetValue(a.Id, out var old) && old.ModifiedUtc != a.ModifiedUtc)
        .Select(a => (Old: fromById[a.Id], New: a))
        .OrderBy(m => m.New.ModifiedUtc)
        .ToList();

    lines.Add($"=== {fromLabel}  ->  {toLabel} ===");
    lines.Add($"  Added ({added.Count}):");
    foreach (var a in added)
        lines.Add($"    + {Local(a.CreatedUtc)}  [{a.Category}/{a.SubCategory}] \"{a.Name}\" (Institution: {a.Institution})");
    lines.Add($"  Removed ({removed.Count}):");
    foreach (var a in removed)
        lines.Add($"    - {Local(a.CreatedUtc)}  [{a.Category}/{a.SubCategory}] \"{a.Name}\" (Institution: {a.Institution})");
    lines.Add($"  Modified ({modified.Count}):");
    foreach (var (old, @new) in modified)
        lines.Add($"    ~ [{@new.Category}/{@new.SubCategory}] \"{@new.Name}\"  {Local(old.ModifiedUtc)} -> {Local(@new.ModifiedUtc)}");
    lines.Add("");
}

string outPath = Path.Combine(AppContext.BaseDirectory, "comparison-result.txt");
File.WriteAllLines(outPath, lines);

Console.WriteLine();
foreach (var l in lines) Console.WriteLine(l);
Console.WriteLine();
Console.WriteLine($"Saved to: {outPath}");
Console.WriteLine("Press Enter to close.");
Console.ReadLine();

static string ReadMasked()
{
    var sb = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (sb.Length > 0) { sb.Length--; Console.Write("\b \b"); }
            continue;
        }
        if (!char.IsControl(key.KeyChar))
        {
            sb.Append(key.KeyChar);
            Console.Write('*');
        }
    }
    return sb.ToString();
}

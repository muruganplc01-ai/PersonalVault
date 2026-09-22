using System.Text;
using System.Text.Json;
using PersonalVault.Models;

namespace PersonalVault.Utils;

/// <summary>
/// Plain-CSV export/import for accounts, using a single documented schema of our own
/// rather than any particular third-party password manager's proprietary format. To
/// bring data in from another manager, export it to CSV there and line its columns up
/// with the header row below (renaming/reordering as needed), or start from this app's
/// own "Export CSV" output as a ready-made template.
///
/// IMPORTANT: unlike everything else in this app, an exported CSV is plain, unencrypted
/// text sitting wherever you save it - including every password in the clear. Treat it
/// as sensitive and delete it once you're done with it.
/// </summary>
public static class CsvIO
{
    private static readonly string[] Header =
    {
        "Category", "Name", "Institution", "Owner", "UserName", "Password",
        "AccountNumber", "Website", "PhoneNumber", "DueDate", "Recurrence", "Notes", "ExtraFieldsJson"
    };

    public static void Export(string path, IEnumerable<AccountEntry> accounts)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Header));

        foreach (var a in accounts)
        {
            var fields = new[]
            {
                a.Category,
                a.Name,
                a.Institution,
                a.Owner,
                a.UserName,
                a.Password,
                a.AccountNumber,
                a.Website,
                a.PhoneNumber,
                a.DueDate?.ToString("yyyy-MM-dd") ?? "",
                a.Recurrence.ToString(),
                a.Notes,
                JsonSerializer.Serialize(a.ExtraFields)
            };
            sb.AppendLine(string.Join(",", fields.Select(CsvEscape)));
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static List<AccountEntry> Import(string path)
    {
        var rows = SimpleCsv.Parse(File.ReadAllText(path, Encoding.UTF8));
        if (rows.Count == 0) return new List<AccountEntry>();

        var header = rows[0];
        int Idx(string name) => Array.IndexOf(header, name);

        int iCategory = Idx("Category"), iName = Idx("Name"), iInstitution = Idx("Institution"),
            iOwner = Idx("Owner"), iUserName = Idx("UserName"), iPassword = Idx("Password"),
            iAccountNumber = Idx("AccountNumber"), iWebsite = Idx("Website"), iPhone = Idx("PhoneNumber"),
            iDueDate = Idx("DueDate"), iRecurrence = Idx("Recurrence"), iNotes = Idx("Notes"),
            iExtra = Idx("ExtraFieldsJson");

        if (iName < 0)
            throw new InvalidDataException(
                "This CSV doesn't have a 'Name' column, so it doesn't match the expected format. " +
                "See README.md for the header row Personal Vault expects.");

        var result = new List<AccountEntry>();
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Get(int idx) => idx >= 0 && idx < row.Length ? row[idx] : "";

            var entry = new AccountEntry
            {
                Category = string.IsNullOrWhiteSpace(Get(iCategory)) ? AccountCategories.Default : Get(iCategory).Trim(),
                Name = Get(iName),
                Institution = Get(iInstitution),
                Owner = Get(iOwner),
                UserName = Get(iUserName),
                Password = Get(iPassword),
                AccountNumber = Get(iAccountNumber),
                Website = Get(iWebsite),
                PhoneNumber = Get(iPhone),
                Recurrence = Enum.TryParse<RecurrenceType>(Get(iRecurrence), out var rec) ? rec : RecurrenceType.None,
                Notes = Get(iNotes),
            };

            if (DateTime.TryParse(Get(iDueDate), out var due))
                entry.DueDate = due.Date;

            var extraJson = Get(iExtra);
            if (!string.IsNullOrWhiteSpace(extraJson))
            {
                try { entry.ExtraFields = JsonSerializer.Deserialize<Dictionary<string, string>>(extraJson) ?? new(); }
                catch { /* malformed cell - skip extra fields for this one row rather than fail the whole import */ }
            }

            if (!string.IsNullOrWhiteSpace(entry.Name))
                result.Add(entry);
        }

        return result;
    }

    private static string CsvEscape(string? value)
    {
        value ??= "";
        bool needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}

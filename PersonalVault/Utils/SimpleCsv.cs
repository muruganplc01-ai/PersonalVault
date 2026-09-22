using System.Text;

namespace PersonalVault.Utils;

/// <summary>
/// Minimal RFC 4180-style CSV parser: handles quoted fields with embedded commas,
/// newlines, and escaped quotes. Shared by CsvIO (account import/export) and
/// BalanceCsvImporter (reading a brokerage-exported balance/positions file) so there's
/// only one CSV-parsing implementation to keep correct.
/// </summary>
public static class SimpleCsv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var field = new StringBuilder();
        var row = new List<string>();
        bool inQuotes = false;

        void EndField() { row.Add(field.ToString()); field.Clear(); }
        void EndRow() { EndField(); rows.Add(row.ToArray()); row = new List<string>(); }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': EndField(); break;
                case '\r': break; // swallow - newline is handled on '\n'
                case '\n': EndRow(); break;
                default: field.Append(c); break;
            }
        }

        if (field.Length > 0 || row.Count > 0) EndRow();

        return rows.Where(r => r.Length > 1 || !string.IsNullOrEmpty(r[0])).ToList();
    }
}

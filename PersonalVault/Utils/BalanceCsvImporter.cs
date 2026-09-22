using System.Globalization;

namespace PersonalVault.Utils;

/// <summary>
/// Best-effort reader for a balance/positions CSV exported from a brokerage (Schwab,
/// Fidelity, Robinhood, M1, or anything similar). Brokerages don't share a common
/// export format, and this app has no way to verify their exact current schemas, so
/// this deliberately does NOT try to be a precise per-broker parser. Instead it looks
/// for the most "this is a dollar amount" column, prefers an explicit Total row if the
/// file already has one, otherwise sums the column - and hands back both a proposed
/// amount and a plain-language explanation of how it got there.
///
/// ImportBalanceForm always shows this as an editable suggestion and never applies it
/// silently - a wrong guess here is real money, so a human confirms every import.
/// </summary>
public static class BalanceCsvImporter
{
    public class Result
    {
        public decimal? Amount { get; set; }
        public string Explanation { get; set; } = string.Empty;
    }

    // Checked in order - more specific phrases first, so "Market Value" wins over a
    // bare "Value" match elsewhere in the same header row.
    private static readonly string[] PreferredHeaderKeywords =
    {
        "current value", "market value", "account value", "total value",
        "value", "balance", "total"
    };

    // Columns that are dollar-shaped numbers but are NOT the account balance - a
    // share price or a quantity would otherwise easily be mistaken for it in the
    // fallback (no-header-match) heuristic below.
    private static readonly string[] ExcludedHeaderKeywords =
    {
        "quantity", "qty", "shares", "price", "cost basis", "cost", "gain", "loss",
        "percent", "%", "change", "symbol", "cusip", "date", "description"
    };

    public static Result Analyze(string filePath)
    {
        List<string[]> rows;
        try
        {
            rows = SimpleCsv.Parse(File.ReadAllText(filePath));
        }
        catch (Exception ex)
        {
            return new Result { Amount = null, Explanation = "Could not read that file: " + ex.Message };
        }

        if (rows.Count < 2)
        {
            return new Result
            {
                Amount = null,
                Explanation = "That file doesn't look like a CSV with a header row and data - enter the amount manually below."
            };
        }

        var header = rows[0];
        var dataRows = rows.Skip(1).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (dataRows.Count == 0)
        {
            return new Result
            {
                Amount = null,
                Explanation = "No data rows found in that file - enter the amount manually below."
            };
        }

        int targetCol = FindColumnByKeyword(header, PreferredHeaderKeywords);
        string method;

        if (targetCol >= 0)
        {
            method = $"used the \"{header[targetCol].Trim()}\" column";
        }
        else
        {
            targetCol = FindBestCurrencyColumn(header, dataRows);
            method = "no column header matched \"value\"/\"balance\"/\"total\", so the column with the most dollar-looking values was used";
        }

        if (targetCol < 0)
        {
            return new Result
            {
                Amount = null,
                Explanation = "Couldn't find a column that looks like dollar amounts in that file - enter the amount manually below."
            };
        }

        // Prefer an explicit "Total" row over summing everything ourselves, if the
        // file already has one - many brokerage exports do, and summing on top of it
        // would double-count. Checked from the bottom up since a total row is usually
        // the last one.
        for (int r = dataRows.Count - 1; r >= 0; r--)
        {
            var row = dataRows[r];
            bool looksLikeTotalRow = row.Take(Math.Max(1, targetCol))
                .Any(c => c.Contains("total", StringComparison.OrdinalIgnoreCase));
            if (looksLikeTotalRow && targetCol < row.Length && TryParseCurrency(row[targetCol], out var totalValue))
            {
                return new Result
                {
                    Amount = totalValue,
                    Explanation = $"Found a row labeled \"Total\" and {method} from it."
                };
            }
        }

        decimal sum = 0;
        int counted = 0;
        foreach (var row in dataRows)
        {
            if (targetCol < row.Length && TryParseCurrency(row[targetCol], out var value))
            {
                sum += value;
                counted++;
            }
        }

        if (counted == 0)
        {
            return new Result
            {
                Amount = null,
                Explanation = $"Found a likely column ({method}) but none of its values looked like dollar amounts - enter the amount manually below."
            };
        }

        return new Result
        {
            Amount = sum,
            Explanation = $"Added up {counted} row(s), {method}."
        };
    }

    private static int FindColumnByKeyword(string[] header, string[] keywords)
    {
        foreach (var keyword in keywords)
            for (int i = 0; i < header.Length; i++)
                if (header[i].Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return i;
        return -1;
    }

    private static int FindBestCurrencyColumn(string[] header, List<string[]> dataRows)
    {
        int bestCol = -1;
        int bestCount = 0;

        for (int col = 0; col < header.Length; col++)
        {
            if (ExcludedHeaderKeywords.Any(k => header[col].Contains(k, StringComparison.OrdinalIgnoreCase)))
                continue;

            int count = dataRows.Count(row => col < row.Length && TryParseCurrency(row[col], out _));
            if (count > bestCount)
            {
                bestCount = count;
                bestCol = col;
            }
        }

        return bestCount > 0 ? bestCol : -1;
    }

    /// <summary>Parses "$1,234.56", "(1,234.56)" (accounting negative), "-1234.56", etc. Rejects percentages and blanks.</summary>
    private static bool TryParseCurrency(string? raw, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var s = raw.Trim();
        if (s.Contains('%')) return false;

        bool negative = false;
        if (s.StartsWith("(") && s.EndsWith(")"))
        {
            negative = true;
            s = s[1..^1];
        }

        s = s.Replace("$", "").Replace(",", "").Trim();
        if (s.StartsWith("-"))
        {
            negative = true;
            s = s[1..];
        }

        if (s.Length == 0 || !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return false;

        value = negative ? -parsed : parsed;
        return true;
    }
}

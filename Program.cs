using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            Log("Starting the scraping process...");

            // Load the webpage
            string url = "https://tck.gorselpanel.com/task/hareket.html";
            var web = new HtmlWeb();
            var document = web.Load(url);
            Log("Webpage loaded successfully.");

            // Parse the table
            var rows = document.DocumentNode.SelectNodes("//table//tr");
            if (rows == null || rows.Count == 0)
            {
                Log("No data found on the webpage.");
                return;
            }
            Log($"{rows.Count - 1} transactions found in the table.");

            // Extract transactions
            var transactions = new List<Transaction>();
            foreach (var row in rows.Skip(1)) // Skip the header row
            {
                var cells = row.SelectNodes("td").Select(td => td.InnerText.Trim()).ToArray();
                if (cells.Length < 8) continue;

                transactions.Add(new Transaction
                {
                    ID = int.Parse(cells[0]),
                    TransID = cells[1],
                    CustomerCode = cells[2],
                    Zaman = ToDateTime(cells[3]),
                    YatirimTipi = ToInt(cells[4]),
                    Tutar = ToDecimal(cells[5]),
                    IsDeposit = ToString(cells[6]) == "1",
                    IsWithdraw = ToString(cells[7]) == "1"
                });
            }
            Log("Transactions successfully parsed.");

            // Sort transactions by date
            transactions = transactions.OrderByDescending(t => t.Zaman).ToList();

            // Step 1: Get the latest deposit date
            var latestDeposit = transactions.FirstOrDefault(t => t.IsDeposit);
            if (latestDeposit == null)
            {
                Log("No deposit transactions found.");
                return;
            }
            Log($"Latest deposit found: ID {latestDeposit.ID}, Time {latestDeposit.Zaman}");

            // Step 2: Find deposits within 48 hours from the latest deposit
            DateTime latestDepositTime = latestDeposit.Zaman;
            var depositsWithin48Hours = transactions
                .Where(t => t.IsDeposit && t.Zaman >= latestDepositTime.AddHours(-48))
                .OrderBy(t => t.Zaman)
                .ToList();

            if (!depositsWithin48Hours.Any())
            {
                Log("No deposits found within the last 48 hours.");
                return;
            }
            var oldestDepositWithin48Hours = depositsWithin48Hours.First();
            Log($"Oldest deposit within 48 hours: ID {oldestDepositWithin48Hours.ID}, Time {oldestDepositWithin48Hours.Zaman}");

            // Step 3: Find the oldest withdraw 24 hours before the oldest deposit
            DateTime withdrawSearchTime = oldestDepositWithin48Hours.Zaman.AddHours(-24);
            var oldestWithdraw = transactions
                .Where(t => t.IsWithdraw && t.Zaman <= withdrawSearchTime)
                .OrderBy(t => t.Zaman)
                .FirstOrDefault();

            if (oldestWithdraw == null)
            {
                Log("No withdraws found within the required timeframe.");
                return;
            }
            Log($"Oldest withdraw found: ID {oldestWithdraw.ID}, Time {oldestWithdraw.Zaman}");

            // Step 4: Filter deposits and withdraws excluding Payment Type ID 76
            var relevantTransactions = transactions
                .Where(t => t.Zaman >= oldestWithdraw.Zaman && t.YatirimTipi != 76)
                .ToList();
            Log($"{relevantTransactions.Count} relevant transactions found after filtering Payment Type ID 76.");

            // Step 5: Perform deposit/withdraw calculations
            var deposits = relevantTransactions.Where(t => t.IsDeposit).OrderBy(t => t.Zaman).ToList();
            var withdraws = relevantTransactions.Where(t => t.IsWithdraw).OrderBy(t => t.Zaman).ToList();
            Log($"Processing {deposits.Count} deposits and {withdraws.Count} withdraws.");

            decimal remaining = 0;

            foreach (var deposit in deposits)
            {
                decimal depositAmount = deposit.Tutar + remaining;

                foreach (var withdraw in withdraws.ToList())
                {
                    if ((withdraw.Zaman - deposit.Zaman).TotalHours > 24) continue;

                    if (depositAmount <= 0) break;

                    if (depositAmount >= withdraw.Tutar)
                    {
                        depositAmount -= withdraw.Tutar;
                        withdraws.Remove(withdraw);
                    }
                    else
                    {
                        withdraw.Tutar -= depositAmount;
                        depositAmount = 0;
                    }
                }

                remaining = depositAmount;
            }

            Log($"Calculation complete. Remaining amount after processing: {remaining}");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
    }

    // Logging mechanism
    static void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
    }

    #region PrivateMethods

    public static DateTime ToDateTime(object input, DateTime defaultDateTime = default)
    {
        try
        {
            if (input == null || string.IsNullOrWhiteSpace(input.ToString())) return defaultDateTime;
            return DateTime.TryParseExact(
                input.ToString(),
                "yyyy/MM/dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result
            ) ? result : defaultDateTime;
        }
        catch
        {
            return defaultDateTime;
        }
    }
    public static int ToInt(object input, int defaultInt = 0)
    {
        try
        {
            if (input == null || string.IsNullOrWhiteSpace(input.ToString())) return defaultInt;
            return int.TryParse(input.ToString(), out var result) ? result : defaultInt;
        }
        catch
        {
            return defaultInt;
        }
    }
    public static decimal ToDecimal(object input, decimal defaultDecimal = 0)
    {
        try
        {
            if (input == null || string.IsNullOrWhiteSpace(input.ToString())) return defaultDecimal;
            return decimal.TryParse(input.ToString(), out var result) ? result : defaultDecimal;
        }
        catch
        {
            return defaultDecimal;
        }
    }
    public static string ToString(object input, string defaultString = "")
    {
        try
        {
            return input?.ToString() ?? defaultString;
        }
        catch
        {
            return defaultString;
        }
    }

    #endregion
}

// Transaction Model
class Transaction
{
    public int ID { get; set; }
    public string TransID { get; set; }
    public string CustomerCode { get; set; }
    public DateTime Zaman { get; set; }
    public int YatirimTipi { get; set; }
    public decimal Tutar { get; set; }
    public bool IsDeposit { get; set; }
    public bool IsWithdraw { get; set; }
}

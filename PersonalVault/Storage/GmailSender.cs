using System.Text;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using PersonalVault.Utils;
using GmailMessage = Google.Apis.Gmail.v1.Data.Message;

namespace PersonalVault.Storage;

/// <summary>
/// Sends the one-time email code for Email MFA, using the exact same OAuth credential
/// GoogleDriveSync already authorized (see its Credential property) - this is why
/// Email MFA requires an active Google Drive sign-in with the gmail.send scope granted;
/// there's no other zero-cost way to send mail from this app. Only ever sends one kind
/// of message: a short plaintext code to the address the user set in Profile.
/// </summary>
public static class GmailSender
{
    public static async Task SendCodeAsync(GoogleDriveSync drive, string toAddress, string code)
    {
        if (drive.Credential == null)
            throw new InvalidOperationException("Not signed in to Google Drive - Email MFA needs an active Drive sign-in to send mail.");

        var gmail = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = drive.Credential,
            ApplicationName = "Personal Vault"
        });

        string subject = "Personal Vault verification code";
        string body = $"Your Personal Vault verification code is: {code}\r\n\r\nThis code expires in 10 minutes. If you didn't request this, someone may have your master password - consider changing it.";

        string raw = $"To: {toAddress}\r\nSubject: {subject}\r\nContent-Type: text/plain; charset=UTF-8\r\n\r\n{body}";
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var message = new GmailMessage { Raw = encoded };

        try
        {
            await gmail.Users.Messages.Send(message, "me").ExecuteAsync();
            DebugLog.Write("GmailSender.SendCodeAsync: sent successfully.");
        }
        catch (Exception ex)
        {
            DebugLog.WriteException("GmailSender.SendCodeAsync", ex);
            throw;
        }
    }
}

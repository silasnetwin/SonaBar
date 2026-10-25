namespace SonaBar.App;

/// <summary>
/// Keeps the Windows tray tooltip inside the 63-character limit enforced by
/// <see cref="System.Windows.Forms.NotifyIcon"/>. The prefix length is measured
/// from the composed string so a short status can never overrun it.
/// </summary>
public static class TrayText
{
    public const int MaxLength = 63;
    private const string Prefix = "SonaBar · ";

    public static string ForStatus(string status)
    {
        var text = Prefix + status;
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using PersonalVault.Models;

namespace PersonalVault.Forms;

/// <summary>
/// Edits the vault owner's display name and picture (VaultData.Profile). Both are
/// mutated directly on the passed-in VaultProfile when the user clicks Save - same
/// "mutate in place, caller decides whether to persist" pattern as AccountEditForm.
///
/// The picture never leaves this app except inside the encrypted vault file itself -
/// there is deliberately no separate upload of it anywhere.
/// </summary>
public class ProfileForm : Form
{
    private const int PictureSize = 128;

    private readonly VaultProfile _profile;

    // null = picture unchanged from what was passed in; empty array = explicitly removed.
    private byte[]? _pendingPictureBytes;

    private readonly PictureBox _pictureBox = new()
    {
        Width = PictureSize,
        Height = PictureSize,
        Location = new Point(20, 20),
        SizeMode = PictureBoxSizeMode.Zoom,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly TextBox _nameBox = new() { Location = new Point(20, 182), Width = 340 };
    private readonly TextBox _browserPathBox = new() { Location = new Point(20, 268), Width = 250, ReadOnly = true };

    /// <summary>
    /// The chosen default-browser .exe path, or null to mean "use Windows' normal
    /// default browser" - set once the user clicks Save. AppSettings.DefaultBrowserPath
    /// isn't touched directly by this form; the caller (MainForm/TrayApplicationContext)
    /// reads this back out and persists it, same "mutate/return, caller decides whether
    /// to persist" pattern as everything else here.
    /// </summary>
    public string? SelectedBrowserPath { get; private set; }

    public ProfileForm(VaultProfile profile, string? currentDefaultBrowserPath = null)
    {
        _profile = profile;

        Text = "Your Profile";
        Width = 400;
        Height = 390;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        Controls.Add(_pictureBox);

        var chooseBtn = new Button { Text = "Choose Picture...", AutoSize = true, Location = new Point(170, 20) };
        var removeBtn = new Button { Text = "Remove Picture", AutoSize = true, Location = new Point(170, 55) };
        chooseBtn.Click += ChooseBtn_Click;
        removeBtn.Click += (_, _) =>
        {
            _pendingPictureBytes = Array.Empty<byte>();
            _pictureBox.Image?.Dispose();
            _pictureBox.Image = null;
        };
        Controls.Add(chooseBtn);
        Controls.Add(removeBtn);

        Controls.Add(new Label { Text = "Name:", AutoSize = true, Location = new Point(20, 162) });
        Controls.Add(_nameBox);

        Controls.Add(new Label { Text = "Default browser:", AutoSize = true, Location = new Point(20, 220) });
        Controls.Add(new Label
        {
            Text = "Used when opening a website link from Personal Vault. Leave blank to use Windows' normal default browser.",
            AutoSize = false,
            Location = new Point(20, 240),
            Width = 350,
            Height = 30,
            ForeColor = Color.DimGray
        });

        var browseBrowserBtn = new Button { Text = "Browse...", AutoSize = true, Location = new Point(280, 267) };
        var useSystemDefaultBtn = new Button { Text = "Use System Default", AutoSize = true, Location = new Point(20, 296) };
        browseBrowserBtn.Click += BrowseBrowserBtn_Click;
        useSystemDefaultBtn.Click += (_, _) => _browserPathBox.Text = string.Empty;
        Controls.Add(_browserPathBox);
        Controls.Add(browseBrowserBtn);
        Controls.Add(useSystemDefaultBtn);

        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, Location = new Point(230, 335) };
        var saveButton = new Button { Text = "Save", AutoSize = true, Location = new Point(315, 335) };
        saveButton.Click += SaveButton_Click;
        Controls.Add(cancelButton);
        Controls.Add(saveButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        _nameBox.Text = _profile.Name;
        _browserPathBox.Text = currentDefaultBrowserPath ?? string.Empty;
        if (_profile.HasPicture)
        {
            try
            {
                var bytes = Convert.FromBase64String(_profile.PictureBase64);
                using var ms = new MemoryStream(bytes);
                _pictureBox.Image = Image.FromStream(ms);
            }
            catch
            {
                // Stored picture is somehow corrupt/unreadable - just fall back to the empty placeholder.
            }
        }
    }

    private void ChooseBtn_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            Title = "Choose a profile picture"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var pngBytes = ResizeToSquarePng(dialog.FileName, PictureSize);
            _pendingPictureBytes = pngBytes;

            using var ms = new MemoryStream(pngBytes);
            _pictureBox.Image?.Dispose();
            _pictureBox.Image = Image.FromStream(ms);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not load that image: " + ex.Message, "Personal Vault",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BrowseBrowserBtn_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Applications (*.exe)|*.exe|All files|*.*",
            Title = "Choose a browser"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _browserPathBox.Text = dialog.FileName;
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        _profile.Name = _nameBox.Text.Trim();

        if (_pendingPictureBytes != null)
        {
            _profile.PictureBase64 = _pendingPictureBytes.Length == 0
                ? string.Empty
                : Convert.ToBase64String(_pendingPictureBytes);
        }

        var browserPath = _browserPathBox.Text.Trim();
        SelectedBrowserPath = string.IsNullOrEmpty(browserPath) ? null : browserPath;

        DialogResult = DialogResult.OK;
    }

    /// <summary>Loads an image file and crops/scales it to fill a size x size square - a standard avatar treatment.</summary>
    private static byte[] ResizeToSquarePng(string path, int size)
    {
        using var original = Image.FromFile(path);
        using var square = new Bitmap(size, size);
        using (var g = Graphics.FromImage(square))
        {
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;

            float scale = Math.Max((float)size / original.Width, (float)size / original.Height);
            int drawWidth = (int)Math.Round(original.Width * scale);
            int drawHeight = (int)Math.Round(original.Height * scale);
            int offsetX = (size - drawWidth) / 2;
            int offsetY = (size - drawHeight) / 2;

            g.DrawImage(original, offsetX, offsetY, drawWidth, drawHeight);
        }

        using var ms = new MemoryStream();
        square.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}

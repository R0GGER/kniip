using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kniip.Editor;
using Kniip.Infrastructure;
using Kniip.Resources;
using Kniip.Settings;

namespace Kniip.Upload;

/// <summary>Bewerkt één uploadbestemming; werkt op een kopie die pas bij OK wordt teruggegeven.</summary>
public partial class UploaderEditorWindow : Window
{
    private readonly UploadDestination _destination;
    private readonly ObservableCollection<PairRow> _parameters = new();
    private readonly ObservableCollection<PairRow> _headers = new();
    private readonly ObservableCollection<PairRow> _arguments = new();

    public UploaderEditorWindow(UploadDestination destination)
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        _destination = SettingsStore.DeepCopy(destination);
        MaxHeight = SystemParameters.WorkArea.Height;

        SetupGrid(ParametersGrid, _parameters);
        SetupGrid(HeadersGrid, _headers);
        SetupGrid(ArgumentsGrid, _arguments);
        SyntaxHelpText.Text = Strings.UploaderEditor_SyntaxHelpText;

        foreach (CustomRequestMethod method in Enum.GetValues<CustomRequestMethod>())
        {
            MethodCombo.Items.Add(new ComboBoxItem { Content = method.ToString(), Tag = method });
        }

        foreach ((CustomBodyType body, string label) in new[]
                 {
                     (CustomBodyType.None, Strings.UploaderEditor_BodyNone),
                     (CustomBodyType.MultipartFormData, "Form data (multipart/form-data)"),
                     (CustomBodyType.FormURLEncoded, "Form URL encoded"),
                     (CustomBodyType.JSON, "JSON"),
                     (CustomBodyType.XML, "XML"),
                     (CustomBodyType.Binary, "Binary")
                 })
        {
            BodyCombo.Items.Add(new ComboBoxItem { Content = label, Tag = body });
        }

        foreach ((FtpEncryption encryption, string label) in new[]
                 {
                     (FtpEncryption.Explicit, Strings.UploaderEditor_EncryptionExplicit),
                     (FtpEncryption.Implicit, Strings.UploaderEditor_EncryptionImplicit),
                     (FtpEncryption.None, Strings.UploaderEditor_EncryptionNone)
                 })
        {
            EncryptionCombo.Items.Add(new ComboBoxItem { Content = label, Tag = encryption });
        }

        Load();
    }

    /// <summary>De bewerkte bestemming; alleen geldig als ShowDialog true teruggeeft.</summary>
    public UploadDestination Destination => _destination;

    public static string TypeLabel(UploadDestination destination) => destination switch
    {
        CustomUploaderDestination => Strings.Upload_TypeCustom,
        _ => destination.TypeName
    };

    private static void SetupGrid(DataGrid grid, ObservableCollection<PairRow> rows)
    {
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Strings.Common_Name,
            Binding = new Binding(nameof(PairRow.Name)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Strings.UploaderEditor_Value,
            Binding = new Binding(nameof(PairRow.Value)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });
        grid.ItemsSource = rows;
    }

    private void Load()
    {
        NameBox.Text = _destination.Name;
        TypeText.Text = TypeLabel(_destination);
        CustomPanel.Visibility = _destination is CustomUploaderDestination ? Visibility.Visible : Visibility.Collapsed;
        ServerPanel.Visibility = _destination is FileServerDestination ? Visibility.Visible : Visibility.Collapsed;

        switch (_destination)
        {
            case CustomUploaderDestination custom:
                Select(MethodCombo, custom.RequestMethod);
                RequestUrlBox.Text = custom.RequestURL;
                Fill(_parameters, custom.Parameters);
                Fill(_headers, custom.Headers);
                Fill(_arguments, custom.Arguments);
                Select(BodyCombo, custom.Body);
                FileFormNameBox.Text = custom.FileFormName;
                DataBox.Text = custom.Data;
                UrlBox.Text = custom.URL;
                ThumbnailUrlBox.Text = custom.ThumbnailURL;
                DeletionUrlBox.Text = custom.DeletionURL;
                ErrorMessageBox.Text = custom.ErrorMessage;
                ShowBodyFields();
                break;
            case FileServerDestination server:
                HostBox.Text = server.Host;
                PortBox.Text = server.Port > 0 ? server.Port.ToString(CultureInfo.InvariantCulture) : string.Empty;
                UsernameBox.Text = server.Username;
                PasswordBox.Password = server.Password;
                RemoteFolderBox.Text = server.RemoteFolder;
                PublicUrlBox.Text = server.PublicUrl;
                FtpOptions.Visibility = server is FtpDestination ? Visibility.Visible : Visibility.Collapsed;
                SshOptions.Visibility = server is SshDestination ? Visibility.Visible : Visibility.Collapsed;
                HostKeyRow.Visibility = server is SshDestination ? Visibility.Visible : Visibility.Collapsed;
                if (server is FtpDestination ftp)
                {
                    Select(EncryptionCombo, ftp.Encryption);
                    PassiveBox.IsChecked = ftp.Passive;
                }

                if (server is SshDestination ssh)
                {
                    PrivateKeyBox.Text = ssh.PrivateKeyPath;
                    PassphraseBox.Password = ssh.Passphrase;
                }

                ShowPortHint();
                ShowHostKey();
                break;
        }
    }

    private static void Fill(ObservableCollection<PairRow> rows, Dictionary<string, string> values)
    {
        rows.Clear();
        foreach ((string name, string value) in values)
        {
            rows.Add(new PairRow { Name = name, Value = value });
        }
    }

    private static void Select<T>(ComboBox combo, T value)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, value)) ?? combo.Items[0];
    }

    private static T Selected<T>(ComboBox combo, T fallback) =>
        combo.SelectedItem is ComboBoxItem { Tag: T value } ? value : fallback;

    private void BodyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowBodyFields();

    private void ShowBodyFields()
    {
        if (FileFormNameRow == null)
        {
            return;
        }

        CustomBodyType body = Selected(BodyCombo, CustomBodyType.MultipartFormData);
        FileFormNameRow.Visibility = body == CustomBodyType.MultipartFormData ? Visibility.Visible : Visibility.Collapsed;
        ArgumentsRow.Visibility = body is CustomBodyType.MultipartFormData or CustomBodyType.FormURLEncoded ? Visibility.Visible : Visibility.Collapsed;
        DataRow.Visibility = body is CustomBodyType.JSON or CustomBodyType.XML ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EncryptionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowPortHint();

    private void ShowPortHint()
    {
        if (PortBox == null || _destination is not FileServerDestination server)
        {
            return;
        }

        int port = server is FtpDestination
            ? Selected(EncryptionCombo, FtpEncryption.Explicit) == FtpEncryption.Implicit ? 990 : 21
            : server.DefaultPort;
        PortBox.ToolTip = string.Format(Strings.UploaderEditor_PortDefault, port);
    }

    private void ShowHostKey()
    {
        string fingerprint = (_destination as SshDestination)?.HostKeyFingerprint ?? string.Empty;
        HostKeyText.Text = fingerprint.Length > 0 ? fingerprint : Strings.UploaderEditor_HostKeyUnknown;
        HostKeyText.SetResourceReference(TextBlock.ForegroundProperty, fingerprint.Length > 0 ? "InkBrush" : "SubtleTextBrush");
        ForgetHostKeyButton.IsEnabled = fingerprint.Length > 0;
    }

    private void ForgetHostKey_Click(object sender, RoutedEventArgs e)
    {
        if (_destination is SshDestination ssh)
        {
            ssh.HostKeyFingerprint = string.Empty;
            ShowHostKey();
        }
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        string sshFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.UploaderEditor_PrivateKey,
            Filter = Strings.UploaderEditor_KeyFilter,
            InitialDirectory = System.IO.Directory.Exists(sshFolder) ? sshFolder : string.Empty
        };
        if (dialog.ShowDialog(this) == true)
        {
            PrivateKeyBox.Text = dialog.FileName;
        }
    }

    /// <summary>Zet het formulier in de kopie; geeft een foutmelding terug als iets niet klopt.</summary>
    private string? Apply()
    {
        _destination.Name = NameBox.Text.Trim();
        switch (_destination)
        {
            case CustomUploaderDestination custom:
                ParametersGrid.CommitEdit(DataGridEditingUnit.Row, true);
                HeadersGrid.CommitEdit(DataGridEditingUnit.Row, true);
                ArgumentsGrid.CommitEdit(DataGridEditingUnit.Row, true);
                custom.RequestMethod = Selected(MethodCombo, CustomRequestMethod.POST);
                custom.RequestURL = RequestUrlBox.Text.Trim();
                custom.Parameters = ToDictionary(_parameters);
                custom.Headers = ToDictionary(_headers);
                custom.Arguments = ToDictionary(_arguments);
                custom.Body = Selected(BodyCombo, CustomBodyType.MultipartFormData);
                custom.FileFormName = FileFormNameBox.Text.Trim();
                custom.Data = DataBox.Text;
                custom.URL = UrlBox.Text.Trim();
                custom.ThumbnailURL = ThumbnailUrlBox.Text.Trim();
                custom.DeletionURL = DeletionUrlBox.Text.Trim();
                custom.ErrorMessage = ErrorMessageBox.Text.Trim();
                if (!custom.IsConfigured)
                {
                    return Strings.UploaderEditor_NeedUrl;
                }

                if (custom.Body == CustomBodyType.MultipartFormData && custom.FileFormName.Length == 0)
                {
                    return Strings.UploaderEditor_NeedFileFormName;
                }

                break;
            case FileServerDestination server:
                server.Host = HostBox.Text.Trim();
                server.Username = UsernameBox.Text.Trim();
                server.Password = PasswordBox.Password;
                server.RemoteFolder = RemoteFolderBox.Text.Trim();
                server.PublicUrl = PublicUrlBox.Text.Trim();
                string portText = PortBox.Text.Trim();
                if (portText.Length == 0)
                {
                    server.Port = 0;
                }
                else if (int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65535)
                {
                    server.Port = port;
                }
                else
                {
                    return Strings.UploaderEditor_BadPort;
                }

                if (server is FtpDestination ftp)
                {
                    ftp.Encryption = Selected(EncryptionCombo, FtpEncryption.Explicit);
                    ftp.Passive = PassiveBox.IsChecked == true;
                }

                if (server is SshDestination ssh)
                {
                    ssh.PrivateKeyPath = PrivateKeyBox.Text.Trim();
                    ssh.Passphrase = PassphraseBox.Password;
                }

                if (!server.IsConfigured)
                {
                    return Strings.UploaderEditor_NeedHost;
                }

                if (server.PublicUrl.Length > 0 && !Uri.TryCreate(server.PublicUrl.Replace("{filename}", "x", StringComparison.OrdinalIgnoreCase),
                        UriKind.Absolute, out _))
                {
                    return Strings.UploaderEditor_BadPublicUrl;
                }

                break;
        }

        return null;
    }

    private static Dictionary<string, string> ToDictionary(IEnumerable<PairRow> rows)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (PairRow row in rows)
        {
            if (row.Name.Trim().Length > 0)
            {
                result[row.Name.Trim()] = row.Value;
            }
        }

        return result;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Apply() is { } error)
        {
            MessageBox.Show(this, error, "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (Apply() is { } error)
        {
            MessageBox.Show(this, error, "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TestButton.IsEnabled = false;
        TestOutput.Visibility = Visibility.Collapsed;
        TestStatus.Text = Strings.Settings_TestUploading;
        try
        {
            using System.IO.MemoryStream png = ImageExporter.EncodePng(UploadTestImage.Create());
            UploadResult result = await UploaderFactory.UploadAsync(_destination, png, "kniip-test.png", "image/png");
            bool copied = ClipboardWriter.SetText(result.Url);
            TestStatus.Text = Strings.Settings_TestSucceeded + result.Url + (copied ? Strings.Settings_TestCopied : string.Empty);
            TestStatus.ToolTip = result.Url;
            var details = new List<string> { "URL: " + result.Url };
            if (result.ThumbnailUrl != null)
            {
                details.Add(Strings.UploaderEditor_ThumbnailUrl + ": " + result.ThumbnailUrl);
            }

            if (result.DeletionUrl != null)
            {
                details.Add(Strings.UploaderEditor_DeletionUrl + ": " + result.DeletionUrl);
            }

            ShowOutput(string.Join(Environment.NewLine, details) + (result.Response != null ? Environment.NewLine + Environment.NewLine + result.Response : string.Empty));
            if (result.DestinationChanged)
            {
                ShowHostKey();
            }
        }
        catch (OperationCanceledException)
        {
            TestStatus.Text = string.Empty;
        }
        catch (Exception ex)
        {
            TestStatus.Text = Strings.Editor_UploadFailedShort;
            TestStatus.ToolTip = ex.Message;
            ShowOutput(ex.Message + (ex is UploadException { Response: { } response } ? Environment.NewLine + Environment.NewLine + response : string.Empty));
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void ShowOutput(string text)
    {
        TestOutput.Text = text;
        TestOutput.Visibility = Visibility.Visible;
    }

    public sealed class PairRow
    {
        public string Name { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;
    }
}

/// <summary>Een klein effen plaatje in de accentkleur om een bestemming mee te testen.</summary>
internal static class UploadTestImage
{
    public static BitmapSource Create()
    {
        const int size = 64;
        Color accent = Application.Current.Resources["AccentBrush"] is SolidColorBrush brush ? brush.Color : Colors.SteelBlue;
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = accent.B;
            pixels[i + 1] = accent.G;
            pixels[i + 2] = accent.R;
            pixels[i + 3] = 255;
        }

        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using mcp_overlord.Logging;

namespace mcp_overlord.UI
{
    /// <summary>One image in the log detail panel.</summary>
    public sealed class LogImagePreview
    {
        /// <summary>Dimensions, size, and type.</summary>
        public string Caption { get; init; } = "";

        /// <summary>Preview. Null when the bytes are not a readable image.</summary>
        public ImageSource? Image { get; init; }

        /// <summary>Bytes written by Save image.</summary>
        public byte[] Bytes { get; init; } = Array.Empty<byte>();

        /// <summary>File name offered in the save dialog.</summary>
        public string SuggestedFileName { get; init; } = "image.png";
    }

    /// <summary>
    /// The panel under the log. It follows the selected line and shows the connection, the body, and any images.
    /// </summary>
    public sealed class LogDetailViewModel : INotifyPropertyChanged
    {
        private readonly IUserInteraction _interaction;
        private readonly IClipboardService _clipboard;
        private readonly RelayCommand _saveImageCommand;
        private LogPayloadView _payload = new LogPayloadView();
        private string _bodyView = "Json";
        private int _tabIndex;
        private LogImagePreview? _selectedImage;

        /// <summary>
        /// Creates the panel.
        /// </summary>
        /// <param name="interaction">Save dialog and error messages.</param>
        /// <param name="clipboard">Copies a field value.</param>
        public LogDetailViewModel(IUserInteraction interaction, IClipboardService clipboard)
        {
            _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            Fields = new ObservableCollection<LogPayloadField>();
            Images = new ObservableCollection<LogImagePreview>();
            CopyTextCommand = new RelayCommand<string>(CopyText);
            _saveImageCommand = new RelayCommand(SaveSelectedImage, () => SelectedImage != null && SelectedImage.Bytes.Length > 0);
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Summary rows.</summary>
        public ObservableCollection<LogPayloadField> Fields { get; }

        /// <summary>Images on the selected line.</summary>
        public ObservableCollection<LogImagePreview> Images { get; }

        /// <summary>Copies the command parameter.</summary>
        public ICommand CopyTextCommand { get; }

        /// <summary>Writes the selected image.</summary>
        public ICommand SaveImageCommand
        {
            get { return _saveImageCommand; }
        }

        /// <summary>True when a line is selected.</summary>
        public bool HasRow
        {
            get { return _payload.HasRow; }
        }

        /// <summary>Shown when the line has no connection.</summary>
        public string NetworkNote
        {
            get { return _payload.NetworkNote; }
        }

        /// <summary>Headers for the open request or response tab.</summary>
        public string ActiveHeaders
        {
            get { return _tabIndex == 0 ? _payload.RequestHeaders : _payload.ResponseHeaders; }
        }

        /// <summary>Note for the open request or response tab.</summary>
        public string ActiveNote
        {
            get { return _tabIndex == 0 ? _payload.RequestNote : _payload.ResponseNote; }
        }

        /// <summary>Formatted JSON for the open tab.</summary>
        public string ActiveJson
        {
            get { return _tabIndex == 0 ? _payload.RequestJson : _payload.ResponseJson; }
        }

        /// <summary>Hex lines for the open tab.</summary>
        public ComputedLineList ActiveHexLines
        {
            get { return _tabIndex == 0 ? _payload.RequestHexLines : _payload.ResponseHexLines; }
        }

        /// <summary>Text lines for the open tab.</summary>
        public ComputedLineList ActiveTextLines
        {
            get { return _tabIndex == 0 ? _payload.RequestTextLines : _payload.ResponseTextLines; }
        }

        /// <summary><c>Json</c>, <c>Hex</c>, or <c>Ascii</c>.</summary>
        public string BodyView
        {
            get { return _bodyView; }
            set
            {
                string next = NormalizeView(value);
                if (string.Equals(_bodyView, next, StringComparison.Ordinal))
                {
                    return;
                }

                _bodyView = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowJson));
                OnPropertyChanged(nameof(ShowHex));
                OnPropertyChanged(nameof(ShowAscii));
            }
        }

        /// <summary>0 request, 1 response, 2 image.</summary>
        public int TabIndex
        {
            get { return _tabIndex; }
            set
            {
                int next = value < 0 ? 0 : value;
                if (_tabIndex == next)
                {
                    return;
                }

                _tabIndex = next;
                OnPropertyChanged();
                NotifyActive();
            }
        }

        /// <summary>True when the request or response body is the visible tab.</summary>
        public bool ShowBody
        {
            get { return HasRow && _tabIndex != 2; }
        }

        /// <summary>True when the image tab is selected.</summary>
        public bool ShowImages
        {
            get { return HasRow && _tabIndex == 2; }
        }

        /// <summary>True when the JSON view is showing.</summary>
        public bool ShowJson
        {
            get { return ShowBody && _bodyView == "Json"; }
        }

        /// <summary>True when the hex view is showing.</summary>
        public bool ShowHex
        {
            get { return ShowBody && _bodyView == "Hex"; }
        }

        /// <summary>True when the text view is showing.</summary>
        public bool ShowAscii
        {
            get { return ShowBody && _bodyView == "Ascii"; }
        }

        /// <summary>True when the selected line has at least one image.</summary>
        public bool HasImages
        {
            get { return Images.Count > 0; }
        }

        /// <summary>The image the save button writes.</summary>
        public LogImagePreview? SelectedImage
        {
            get { return _selectedImage; }
            set
            {
                if (ReferenceEquals(_selectedImage, value))
                {
                    return;
                }

                _selectedImage = value;
                OnPropertyChanged();
                _saveImageCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Shows <paramref name="row"/>. Null clears the panel.
        /// </summary>
        /// <param name="row">Selected log line.</param>
        public void Show(LogRow? row)
        {
            _payload = LogPayloads.Describe(row?.Entry);
            Fields.Clear();
            foreach (LogPayloadField field in _payload.Fields)
            {
                Fields.Add(field);
            }

            Images.Clear();
            int index = 1;
            foreach (LoggedImage image in _payload.Images)
            {
                Images.Add(CreatePreview(image, index));
                index++;
            }

            SelectedImage = Images.Count == 0 ? null : Images[0];
            OnPropertyChanged(nameof(HasRow));
            OnPropertyChanged(nameof(NetworkNote));
            OnPropertyChanged(nameof(HasImages));
            NotifyActive();
        }

        private void NotifyActive()
        {
            OnPropertyChanged(nameof(ActiveHeaders));
            OnPropertyChanged(nameof(ActiveNote));
            OnPropertyChanged(nameof(ActiveJson));
            OnPropertyChanged(nameof(ActiveHexLines));
            OnPropertyChanged(nameof(ActiveTextLines));
            OnPropertyChanged(nameof(ShowBody));
            OnPropertyChanged(nameof(ShowImages));
            OnPropertyChanged(nameof(ShowJson));
            OnPropertyChanged(nameof(ShowHex));
            OnPropertyChanged(nameof(ShowAscii));
        }

        private void CopyText(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _clipboard.SetText(text);
        }

        private void SaveSelectedImage()
        {
            LogImagePreview? image = SelectedImage;
            if (image == null || image.Bytes.Length == 0)
            {
                return;
            }

            string? path = _interaction.PickSaveImageFile(image.SuggestedFileName);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                File.WriteAllBytes(path, image.Bytes);
            }
            catch (Exception exception)
            {
                _interaction.ShowMessage("Save image", exception.Message);
            }
        }

        private static LogImagePreview CreatePreview(LoggedImage image, int index)
        {
            string extension = image.MimeType.Contains("jpeg", StringComparison.OrdinalIgnoreCase)
                || image.MimeType.Contains("jpg", StringComparison.OrdinalIgnoreCase)
                ? ".jpg"
                : ".png";
            ImageSource? preview = Decode(image.Bytes);
            return new LogImagePreview
            {
                Caption = Caption(image, index, preview != null),
                Image = preview,
                Bytes = image.Bytes,
                SuggestedFileName = "image-" + index + extension
            };
        }

        private static string Caption(LoggedImage image, int index, bool decoded)
        {
            string size = image.Width > 0 && image.Height > 0
                ? image.Width + " x " + image.Height + ", "
                : "";
            string text = "Image " + index + "  " + image.MimeType + "  " + size + image.Bytes.Length + " bytes";
            if (!decoded && image.Bytes.Length > 0)
            {
                text += "  The image could not be decoded.";
            }

            return text;
        }

        private static ImageSource? Decode(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return null;
            }

            try
            {
                var image = new BitmapImage();
                using (var stream = new MemoryStream(bytes))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                }

                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string NormalizeView(string? value)
        {
            if (string.Equals(value, "Hex", StringComparison.OrdinalIgnoreCase))
            {
                return "Hex";
            }

            if (string.Equals(value, "Ascii", StringComparison.OrdinalIgnoreCase))
            {
                return "Ascii";
            }

            return "Json";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

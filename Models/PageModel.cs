using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using Mpdf.Services;

namespace Mpdf.Models;

public sealed class PageModel : INotifyPropertyChanged
{
    private readonly DocumentSession _session;
    private BitmapSource? _bitmap;
    private BitmapSource? _thumbnail;
    private bool _rendering;
    private bool _thumbnailLoading;
    private int _index;
    private double _zoom = 1.0;
    private string _renderError = "";
    private string _label = "";

    public PageModel(DocumentSession session, Guid id, double unrotatedWidth, double unrotatedHeight, int rotation)
    {
        _session = session;
        Id = id;
        UnrotatedWidth = unrotatedWidth;
        UnrotatedHeight = unrotatedHeight;
        Rotation = rotation;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }
    public DocumentSession Session => _session;
    public ObservableCollection<AnnotationModel> Annotations { get; } = new();

    public double UnrotatedWidth { get; }
    public double UnrotatedHeight { get; }
    public int Rotation { get; internal set; }

    public double DisplayWidthPoints => Rotation % 2 == 0 ? UnrotatedWidth : UnrotatedHeight;
    public double DisplayHeightPoints => Rotation % 2 == 0 ? UnrotatedHeight : UnrotatedWidth;

    public double BaseWidth => DisplayWidthPoints * 96.0 / 72.0;
    public double BaseHeight => DisplayHeightPoints * 96.0 / 72.0;

    public int Index
    {
        get => _index;
        internal set
        {
            if (_index == value) return;
            _index = value;
            Raise(nameof(Index));
            Raise(nameof(Number));
            Raise(nameof(DisplayLabel));
        }
    }

    public int Number => _index + 1;

    public string Label
    {
        get => _label;
        internal set
        {
            if (_label == value) return;
            _label = value;
            Raise(nameof(Label));
            Raise(nameof(DisplayLabel));
        }
    }

    public string DisplayLabel => string.IsNullOrEmpty(_label) ? Number.ToString() : _label;

    public double Zoom
    {
        get => _zoom;
        set
        {
            if (Math.Abs(_zoom - value) < 0.0001) return;
            _zoom = value;
            Bitmap = null;
            Raise(nameof(Zoom));
            Raise(nameof(DisplayWidth));
            Raise(nameof(DisplayHeight));
        }
    }

    public double DisplayWidth => BaseWidth * _zoom;
    public double DisplayHeight => BaseHeight * _zoom;

    public double ThumbnailWidth => 116;
    public double ThumbnailHeight => BaseWidth > 0 ? 116 * BaseHeight / BaseWidth : 116;

    public BitmapSource? Bitmap
    {
        get => _bitmap;
        private set
        {
            if (ReferenceEquals(_bitmap, value)) return;
            _bitmap = value;
            Raise(nameof(Bitmap));
        }
    }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail = value;
            Raise(nameof(Thumbnail));
        }
    }

    public bool IsRendering
    {
        get => _rendering;
        private set
        {
            if (_rendering == value) return;
            _rendering = value;
            Raise(nameof(IsRendering));
        }
    }

    public string RenderError
    {
        get => _renderError;
        private set
        {
            if (_renderError == value) return;
            _renderError = value;
            Raise(nameof(RenderError));
        }
    }

    public async Task EnsureRenderedAsync()
    {
        if (Bitmap != null || IsRendering) return;

        IsRendering = true;
        RenderError = "";
        try
        {
            // El zoom puede cambiar mientras la página se está rasterizando.
            // En ese caso se descarta el resultado y se vuelve a pedir al tamaño actual.
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var width = DisplayWidth;
                if (width <= 0) return;

                var bitmap = await _session.RenderPageAsync(Id, width);
                if (bitmap == null)
                {
                    RenderError = "No se pudo mostrar esta página.";
                    return;
                }

                if (Math.Abs(width - DisplayWidth) < 0.5)
                {
                    Bitmap = bitmap;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            RenderError = "No se pudo mostrar la página: " + ex.Message;
        }
        finally
        {
            IsRendering = false;
        }
    }

    public async Task EnsureThumbnailAsync()
    {
        if (Thumbnail != null || _thumbnailLoading) return;
        _thumbnailLoading = true;
        try
        {
            var bitmap = await _session.RenderPageAsync(Id, ThumbnailWidth);
            if (bitmap != null) Thumbnail = bitmap;
        }
        catch
        {
        }
        finally
        {
            _thumbnailLoading = false;
        }
    }

    public void InvalidateBitmap()
    {
        Bitmap = null;
        RenderError = "";
    }

    public void InvalidateAll()
    {
        Bitmap = null;
        Thumbnail = null;
        RenderError = "";
    }

    internal void RaiseRotationChanged()
    {
        Bitmap = null;
        Thumbnail = null;
        Raise(nameof(Rotation));
        Raise(nameof(DisplayWidthPoints));
        Raise(nameof(DisplayHeightPoints));
        Raise(nameof(BaseWidth));
        Raise(nameof(BaseHeight));
        Raise(nameof(DisplayWidth));
        Raise(nameof(DisplayHeight));
        Raise(nameof(ThumbnailHeight));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

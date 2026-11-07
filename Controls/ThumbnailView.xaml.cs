using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Mpdf.Models;

namespace Mpdf.Controls;

public partial class ThumbnailView : UserControl
{
    private PageModel? _page;

    public ThumbnailView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_page != null) _ = _page.EnsureThumbnailAsync();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_page != null) _page.PropertyChanged -= OnPagePropertyChanged;
        _page = DataContext as PageModel;
        if (_page != null)
        {
            _page.PropertyChanged += OnPagePropertyChanged;
            placeholder.Visibility = _page.Thumbnail == null ? Visibility.Visible : Visibility.Collapsed;
            if (IsLoaded) _ = _page.EnsureThumbnailAsync();
        }
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_page == null) return;
        if (e.PropertyName == nameof(PageModel.Thumbnail))
        {
            placeholder.Visibility = _page.Thumbnail == null ? Visibility.Visible : Visibility.Collapsed;
            if (_page.Thumbnail == null && IsLoaded) _ = _page.EnsureThumbnailAsync();
        }
    }
}

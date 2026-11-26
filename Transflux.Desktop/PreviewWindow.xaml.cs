using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ReaKiller.Desktop;

public partial class PreviewWindow : Window
{
    public PreviewWindow(string sourcePath, string beforePng, string afterPng)
    {
        InitializeComponent();
        FileLabel.Text = Path.GetFileName(sourcePath);
        BeforeImage.Source = LoadPng(beforePng);
        AfterImage.Source = LoadPng(afterPng);
    }

    private static BitmapImage LoadPng(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(path));
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
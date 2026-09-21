using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace NFC_System;

// Native lettering stays sharp and readable on the application's dark surfaces.
public sealed class BrandLogo : Grid
{
    public BrandLogo()
    {
        ColumnSpacing = 12;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var image = new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/Branding/Icon.png")),
            Width = 64, Height = 64, Stretch = Stretch.Uniform
        };
        var text = new TextBlock
        {
            Text = "NFC System", FontSize = 32, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center, CharacterSpacing = 0
        };
        SetColumn(text, 1);
        Children.Add(image);
        Children.Add(text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "NFC System");
    }
}

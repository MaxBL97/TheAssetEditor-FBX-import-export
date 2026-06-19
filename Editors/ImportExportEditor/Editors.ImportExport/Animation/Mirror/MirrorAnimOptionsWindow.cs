using System.Windows;
using System.Windows.Controls;

namespace Editors.ImportExport.Animation.Mirror;

public sealed class MirrorAnimOptionsWindow : Window
{
    private const string WindowStyleResource = "CustomWindowStyle";
    private const string BackgroundBrushResource = "ABrush.Tone1.Background.Static";
    private const string ForegroundBrushResource = "ABrush.Foreground.Static";
    private const string MutedForegroundBrushResource = "ABrush.Foreground.Deeper";

    private readonly RadioButton _planeYzRadioButton;
    private readonly RadioButton _planeXzRadioButton;
    private readonly RadioButton _planeXyRadioButton;

    public AnimationMirrorPlane SelectedPlane { get; private set; } = AnimationMirrorPlane.XY;

    public MirrorAnimOptionsWindow()
    {
        Title = "Mirror animation";
        Width = 520;
        MinHeight = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        SetResourceReference(StyleProperty, WindowStyleResource);
        SetResourceReference(BackgroundProperty, BackgroundBrushResource);
        SetResourceReference(ForegroundProperty, ForegroundBrushResource);

        var root = new Grid
        {
            Margin = new Thickness(18)
        };
        root.SetResourceReference(Panel.BackgroundProperty, BackgroundBrushResource);

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = CreateTextBlock(
            "Select the reflection plane.",
            new Thickness(0, 0, 0, 8));
        title.FontWeight = FontWeights.SemiBold;
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var description = CreateTextBlock(
            "A real mirror is a plane reflection, not an axis rotation. For a humanoid01 left/right chirality mirror, use XY plane (invert Z).",
            new Thickness(0, 0, 0, 16));
        description.SetResourceReference(TextBlock.ForegroundProperty, MutedForegroundBrushResource);
        Grid.SetRow(description, 1);
        root.Children.Add(description);

        var radioButtonPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 16)
        };

        _planeYzRadioButton = CreateRadioButton("YZ plane - invert X", false);
        _planeXzRadioButton = CreateRadioButton("XZ plane - invert Y", false);
        _planeXyRadioButton = CreateRadioButton("XY plane - invert Z (humanoid01 left/right)", true);

        radioButtonPanel.Children.Add(_planeYzRadioButton);
        radioButtonPanel.Children.Add(_planeXzRadioButton);
        radioButtonPanel.Children.Add(_planeXyRadioButton);

        Grid.SetRow(radioButtonPanel, 2);
        root.Children.Add(radioButtonPanel);

        var note = CreateTextBlock(
            "Only one plane can be selected. Inverting two axes is a 180 degree rotation, not a mirror/chirality operation.",
            new Thickness(0, 0, 0, 20));
        note.SetResourceReference(TextBlock.ForegroundProperty, MutedForegroundBrushResource);
        Grid.SetRow(note, 3);
        root.Children.Add(note);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okButton = new Button
        {
            Content = "Create",
            Width = 96,
            MinHeight = 28,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okButton.Click += (_, _) => Confirm();

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 96,
            MinHeight = 28,
            IsCancel = true
        };
        cancelButton.Click += (_, _) => DialogResult = false;

        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        Content = root;
    }

    private static TextBlock CreateTextBlock(string text, Thickness margin)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin
        };

        textBlock.SetResourceReference(TextBlock.ForegroundProperty, ForegroundBrushResource);
        return textBlock;
    }

    private static RadioButton CreateRadioButton(string content, bool isChecked)
    {
        var radioButton = new RadioButton
        {
            Content = content,
            IsChecked = isChecked,
            GroupName = "MirrorPlane",
            Margin = new Thickness(0, 4, 0, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 24
        };

        radioButton.SetResourceReference(Control.ForegroundProperty, ForegroundBrushResource);
        return radioButton;
    }

    private void Confirm()
    {
        if (_planeYzRadioButton.IsChecked == true)
            SelectedPlane = AnimationMirrorPlane.YZ;
        else if (_planeXzRadioButton.IsChecked == true)
            SelectedPlane = AnimationMirrorPlane.XZ;
        else
            SelectedPlane = AnimationMirrorPlane.XY;

        DialogResult = true;
    }
}

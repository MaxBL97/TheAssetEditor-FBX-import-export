using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AssetEditor.Themes.Attached 
{
    /// <summary>
    /// Attached properties for showing hint text in empty TextBox and PasswordBox controls.
    /// Set the control Tag property to the hint text.
    /// </summary>
    public static class TextHinting
    {
        public static readonly DependencyProperty ShowWhenFocusedProperty =
            DependencyProperty.RegisterAttached(
                "ShowWhenFocused",
                typeof(bool),
                typeof(TextHinting),
                new FrameworkPropertyMetadata(false));

        public static void SetShowWhenFocused(DependencyObject element, bool value)
        {
            element.SetValue(ShowWhenFocusedProperty, value);
        }

        public static bool GetShowWhenFocused(DependencyObject element)
        {
            return (bool)element.GetValue(ShowWhenFocusedProperty);
        }
    }
}

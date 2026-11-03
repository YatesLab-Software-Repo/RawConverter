using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RawConverter.Gui.Views
{
    /// <summary>
    /// A WinForms-style MessageBox: OK, or Yes/No.
    /// </summary>
    internal class MessageBoxWindow : Window
    {
        private MessageBoxWindow(string text, string title, bool yesNo)
        {
            Title = title;
            CanResize = false;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 6,
            };
            if (yesNo)
            {
                buttons.Children.Add(MakeButton("Yes", true, true));
                buttons.Children.Add(MakeButton("No", false, false));
            }
            else
            {
                buttons.Children.Add(MakeButton("OK", false, true));
            }

            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 16,
                MinWidth = 240,
                MaxWidth = 520,
                Children =
                {
                    new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    buttons,
                },
            };
        }

        private Button MakeButton(string text, bool result, bool isDefault)
        {
            Button button = new Button { Content = text, Width = 75, Height = 23, IsDefault = isDefault, IsCancel = !isDefault || text == "OK" };
            button.Click += (sender, e) => Close(result);
            return button;
        }

        public static Task<bool> Show(Window owner, string text, string title, bool yesNo)
        {
            return new MessageBoxWindow(text, title, yesNo).ShowDialog<bool>(owner);
        }
    }
}

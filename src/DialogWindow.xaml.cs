using System.Windows;
using System.Windows.Input;

namespace Iconic
{
    /// <summary>A small frosted-glass message box that matches the main window.</summary>
    public partial class DialogWindow : Window
    {
        public DialogWindow(string title, string message, string primary, string secondary)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
            OkButton.Content = primary;

            if (string.IsNullOrEmpty(secondary)) CancelButton.Visibility = Visibility.Collapsed;
            else CancelButton.Content = secondary;

            SourceInitialized += (_, _) => Glass.Apply(this);
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            };
            Loaded += (_, _) => OkButton.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }
    }
}

using Microsoft.Win32;
using System.Windows;

namespace LocalContextBuilder
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Settings { get; private set; }

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            Settings = currentSettings;
            
            ModelPathBox.Text = Settings.ModelPath;
            NpuCheckBox.IsChecked = Settings.UseNpu;
            GpuLayersBox.Text = Settings.GpuLayers.ToString();
            ContextSizeBox.Text = Settings.ContextSize.ToString();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "GGUF files (*.gguf)|*.gguf|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                ModelPathBox.Text = dialog.FileName;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Settings.ModelPath = ModelPathBox.Text;
            Settings.UseNpu = NpuCheckBox.IsChecked ?? false;
            
            if (int.TryParse(GpuLayersBox.Text, out int gpuLayers))
                Settings.GpuLayers = gpuLayers;
                
            if (int.TryParse(ContextSizeBox.Text, out int contextSize))
                Settings.ContextSize = contextSize;

            Settings.Save();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

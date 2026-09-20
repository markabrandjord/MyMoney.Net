using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace Walkabout.Dialogs
{
    public class SpikeRow : INotifyPropertyChanged
    {
        private string code;
        public string Code
        {
            get => this.code;
            set { this.code = value; this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class WpfUiGridEditSpike : Window
    {
        public ObservableCollection<SpikeRow> Rows { get; } = new ObservableCollection<SpikeRow>
        {
            new SpikeRow { Code = "USD" },
            new SpikeRow { Code = "EUR" },
        };

        public ObservableCollection<string> Choices { get; } = new ObservableCollection<string> { "USD", "EUR", "GBP" };

        public WpfUiGridEditSpike()
        {
            this.InitializeComponent();
            this.DataContext = this;
            this.SpikeGrid.ItemsSource = this.Rows;
        }
    }
}

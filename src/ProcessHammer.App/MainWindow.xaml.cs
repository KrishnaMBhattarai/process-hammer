using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace ProcessHammer.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Right-clicking a row selects it first, so the context menu always targets the row under the cursor.
    private void DataGridRow_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row) row.IsSelected = true;
    }
}

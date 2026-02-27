using System.IO;
using System.Windows.Controls;
using Microsoft.Win32;
using Tech901.IdPhoto.ViewModels;

namespace Tech901.IdPhoto.App.Views;

public partial class AdminView : UserControl
{
    public AdminView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is AdminViewModel vm)
        {
            vm.RosterFileRequested += OnRosterFileRequested;
            vm.ExportPathRequested += OnExportPathRequested;
            vm.OutputFolderRequested += OnOutputFolderRequested;
        }

        if (e.OldValue is AdminViewModel oldVm)
        {
            oldVm.RosterFileRequested -= OnRosterFileRequested;
            oldVm.ExportPathRequested -= OnExportPathRequested;
            oldVm.OutputFolderRequested -= OnOutputFolderRequested;
        }
    }

    private Task<string?> OnRosterFileRequested()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            Title = "Select Roster CSV"
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    private Task<string?> OnExportPathRequested()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json",
            Title = "Export Session Report"
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    private Task<string?> OnOutputFolderRequested()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Output Directory"
        };

        if (DataContext is AdminViewModel vm && !string.IsNullOrEmpty(vm.OutputDirectory) && Directory.Exists(vm.OutputDirectory))
            dialog.InitialDirectory = vm.OutputDirectory;

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
    }
}

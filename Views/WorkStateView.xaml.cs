using System.Windows;
using System.Windows.Controls;
using Keeper.ViewModels;

namespace Keeper.Views
{
    public partial class WorkStateView : UserControl
    {
        public WorkStateView()
        {
            InitializeComponent();
        }

        private void ItemsHost_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private async void ItemsHost_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;

            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            var viewModel = DataContext as WorkStateViewModel;
            if (paths == null || viewModel == null)
            {
                return;
            }

            await viewModel.AddFilesAsync(paths);
        }
    }
}

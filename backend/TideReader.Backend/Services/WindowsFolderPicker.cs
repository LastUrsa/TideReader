using System.Windows.Forms;

namespace TideReader.Backend.Services;

public sealed class WindowsFolderPicker : IFolderPicker
{
    public Task<string?> ChooseOutputFolderAsync()
    {
        var tcs = new TaskCompletionSource<string?>();
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Choose OBS output folder",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };

                var result = dialog.ShowDialog();
                tcs.TrySetResult(result == DialogResult.OK ? dialog.SelectedPath : null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

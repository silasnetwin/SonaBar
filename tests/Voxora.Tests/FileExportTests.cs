using Voxora.App;
using Xunit;

namespace Voxora.Tests;

public sealed class FileExportTests
{
    [Fact]
    public async Task ExportKeepsExistingFilesAndCleansCancelledDrafts()
    {
        var folder=Path.Combine(Path.GetTempPath(),"SineFlow-export-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var original=Path.Combine(folder,"sample.wav.txt");
            await File.WriteAllTextAsync(original,"Keep me");
            var result=await MainWindow.WriteExportAsync(folder,"sample.wav","TXT","New transcript",CancellationToken.None);
            Assert.Equal("Keep me",await File.ReadAllTextAsync(original));
            Assert.Equal("New transcript",await File.ReadAllTextAsync(result));
            Assert.Equal("sample.wav (1).txt",Path.GetFileName(result));

            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>MainWindow.WriteExportAsync(folder,"cancel.wav","TXT","Partial",cancelled.Token));
            Assert.Equal(2,Directory.GetFiles(folder).Length);
        }
        finally { Directory.Delete(folder,true); }
    }
}

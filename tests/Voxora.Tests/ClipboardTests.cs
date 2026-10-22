using System.Windows.Forms;
using Voxora.WindowsIntegration;
using Xunit;

namespace Voxora.Tests;
[CollectionDefinition("Desktop clipboard",DisableParallelization=true)]
public sealed class ClipboardCollection { }
[Collection("Desktop clipboard")]
public sealed class ClipboardTests
{
    [Fact]
    public void ClipboardRoundTripsTextHtmlRtfImageAndFileList()
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            var clipboard=new ClipboardService();DataObject? original=null;
            try
            {
                original=clipboard.Capture();
                var data=new DataObject();data.SetData(DataFormats.UnicodeText,"Voxora clipboard integration test");data.SetData(DataFormats.Html,"<html><body><b>Voxora</b></body></html>");data.SetData(DataFormats.Rtf,@"{\rtf1\ansi Voxora}");data.SetData(DataFormats.FileDrop,new[]{@"C:\Voxora-clipboard-test.txt"});
                using var bitmap=new System.Drawing.Bitmap(2,2);bitmap.SetPixel(0,0,System.Drawing.Color.Green);data.SetImage(bitmap);
                clipboard.Restore(data);var saved=clipboard.Capture();var sequence=clipboard.SetText("temporary transcription");clipboard.RestoreIfUnchanged(saved,sequence);
                var restored=Clipboard.GetDataObject()!;
                Assert.Equal("Voxora clipboard integration test",Clipboard.GetText());Assert.Equal("<html><body><b>Voxora</b></body></html>",restored.GetData(DataFormats.Html));Assert.Equal(@"{\rtf1\ansi Voxora}",restored.GetData(DataFormats.Rtf));Assert.Equal(new[]{@"C:\Voxora-clipboard-test.txt"}, (string[])restored.GetData(DataFormats.FileDrop)!);
                using var image=Clipboard.GetImage();Assert.NotNull(image);Assert.Equal(2,image.Width);
            }
            catch(Exception error){failure=error;}
            finally{if(original is not null)clipboard.Restore(original);}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(20)));if(failure is not null)throw new InvalidOperationException("Clipboard integration failed",failure);
    }
    [Fact]
    public void ClipboardRestorationDoesNotOverwriteNewUserCopy()
    {
        Exception? failure=null;var thread=new Thread(()=>
        {
            var clipboard=new ClipboardService();DataObject? original=null;
            try{original=clipboard.Capture();clipboard.SetText("before");var saved=clipboard.Capture();var sequence=clipboard.SetText("dictation");clipboard.SetText("new user copy");clipboard.RestoreIfUnchanged(saved,sequence);Assert.Equal("new user copy",clipboard.Text());}
            catch(Exception error){failure=error;}finally{if(original is not null)clipboard.Restore(original);}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(20)));if(failure is not null)throw failure;
    }
}

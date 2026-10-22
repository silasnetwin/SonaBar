using PdfSharp.Drawing;
using PdfSharp.Pdf;
namespace Voxora.Meetings;
public static class MeetingExport
{
    public static void Pdf(MeetingDocument meeting,string path)
    {
        using var document=new PdfDocument();document.Info.Title=meeting.Title;document.Info.Author="SineFlow · SNine Studios";
        var font=new XFont("Segoe UI",10);XGraphics? graphics=null;double y=0;int pageNumber=0;
        void Page(){graphics?.Dispose();var page=document.AddPage();page.Size=PdfSharp.PageSize.A4;graphics=XGraphics.FromPdfPage(page);y=55;pageNumber++;graphics.DrawString("SineFlow  ·  "+pageNumber,font,XBrushes.Gray,new XPoint(48,810));}
        Page();
        foreach(var paragraph in meeting.Text().Split('\n'))
        {
            var line="";
            foreach(var word in paragraph.Split(' '))
            {
                var candidate=line.Length==0?word:line+" "+word;
                if(graphics!.MeasureString(candidate,font).Width>495&&line.Length>0){if(y>770)Page();graphics!.DrawString(line,font,XBrushes.Black,new XPoint(48,y));y+=15;line=word;}else line=candidate;
            }
            if(y>770)Page();graphics!.DrawString(line,font,XBrushes.Black,new XPoint(48,y));y+=15;
        }
        graphics?.Dispose();document.Save(path);
    }
}

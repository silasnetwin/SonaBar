using SherpaOnnx;
using SonaBar.Core;
namespace SonaBar.Meetings;
public sealed class SpeakerDiarization : ISpeakerDiarizationService, IDisposable
{
    private SpeakerEmbeddingExtractor? extractor;
    private readonly List<float[]> centroids=[];
    public Task<IReadOnlyList<TranscriptSegment>> LabelAsync(string wave,IReadOnlyList<TranscriptSegment> segments,string model,CancellationToken token)=>Task.Run<IReadOnlyList<TranscriptSegment>>(()=>
    {
        extractor ??= new SpeakerEmbeddingExtractor(new SpeakerEmbeddingExtractorConfig { Model=model,NumThreads=2,Debug=0,Provider="cpu" });
        using var reader=new NAudio.Wave.AudioFileReader(wave);
        var samples=new float[reader.Length/4];var length=reader.Read(samples,0,samples.Length);samples=samples[..length];var sampleRate=reader.WaveFormat.SampleRate;var output=new List<TranscriptSegment>();
        foreach(var segment in segments)
        {
            token.ThrowIfCancellationRequested();
            int start=Math.Clamp((int)(segment.Start.TotalSeconds*sampleRate),0,samples.Length);
            int end=Math.Clamp((int)(segment.End.TotalSeconds*sampleRate),start,samples.Length);
            if(end-start<sampleRate){output.Add(segment with {Speaker="Unknown speaker"});continue;}
            using var stream=extractor.CreateStream();stream.AcceptWaveform(sampleRate,samples[start..end]);stream.InputFinished();
            if(!extractor.IsReady(stream)){output.Add(segment with {Speaker="Unknown speaker"});continue;}
            var embedding=extractor.Compute(stream);Normalize(embedding);
            var scores=centroids.Select(x=>x.Zip(embedding,(a,b)=>a*b).Sum()).ToArray();
            var best=scores.Length==0?-1:Array.IndexOf(scores,scores.Max());
            if(best<0||scores[best]<.65f){best=centroids.Count;centroids.Add(embedding);}
            else {for(int j=0;j<embedding.Length;j++)centroids[best][j]=.8f*centroids[best][j]+.2f*embedding[j];Normalize(centroids[best]);}
            output.Add(segment with {Speaker=$"Speaker {best+1}"});
        }
        return output;
    },token);
    private static void Normalize(float[] values){var length=Math.Sqrt(values.Sum(x=>(double)x*x));if(length>0)for(int i=0;i<values.Length;i++)values[i]/=(float)length;}
    public void Dispose(){extractor?.Dispose();}
}

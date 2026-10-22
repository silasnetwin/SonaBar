using System.IO;
using System.Text.Json;
using Voxora.Core;
using Voxora.Transcription;
using Voxora.Enhancement;
using Voxora.Models;
using Voxora.Meetings;

if(args.Length<2)throw new ArgumentException("Arguments: input.wav report.json");
var elapsed=System.Diagnostics.Stopwatch.StartNew();
if(args.Contains("--cpu"))LocalEnhancementEngine.ConfigureBackend("CPU");
using var manager=new ModelManager();
var requestedModel=args.FirstOrDefault(x=>x.StartsWith("--model=",StringComparison.Ordinal))?[8..]??"small";
var speechModel=manager.Catalog.First(x=>x.Id==requestedModel);
if(!manager.IsInstalled(speechModel))await manager.DownloadAsync(speechModel,null,default);
WhisperEngine.ConfigureBackend(args.Contains("--gpu")?"GPU / accelerated":"CPU");using var speech=new WhisperEngine();
if(args.Contains("--latency-only"))
{
    if(args.Contains("--greedy"))Console.WriteLine("whisper.cpp production bridge uses its pinned beam-search profile; --greedy is retained for compatibility only.");
    var timer=System.Diagnostics.Stopwatch.StartNew();
    await speech.WarmAsync(manager.PathFor(speechModel),default);
    var loadMs=timer.Elapsed.TotalMilliseconds;timer.Restart();
    var warmResult=await speech.TranscribeAsync(args[0],manager.PathFor(speechModel),"en","",true,default);
    var warmMs=timer.Elapsed.TotalMilliseconds;
    if(string.IsNullOrWhiteSpace(warmResult.Text))throw new InvalidDataException("Latency benchmark produced no transcript.");
    await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new{model=speechModel.Id,backend=args.Contains("--gpu")?"Automatic":"CPU",audioSeconds=AudioDuration(args[0]),coldLoadMs=loadMs,warmRecognitionMs=warmMs,text=warmResult.Text},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"Cold load: {loadMs:F0} ms; warm recognition: {warmMs:F0} ms");
    return;
}
var transcript=await speech.TranscribeAsync(args[0],manager.PathFor(speechModel),"en","",false,default);
if(!transcript.Text.Contains("country",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Speech inference failed reference-audio check.");
// The automatic language setting must recognise the language and still deliver the words.
// whisper.cpp returns right after its detection when it is asked to detect only, so this
// check protects against a bridge that only detects and never transcribes.
var automatic=await speech.TranscribeAsync(args[0],manager.PathFor(speechModel),"auto","",true,default);
if(string.IsNullOrWhiteSpace(automatic.Text))throw new InvalidDataException("Automatic language detection produced no transcript.");
if(!automatic.Text.Contains("country",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Automatic language detection lost the transcript.");
if(automatic.Language!=transcript.Language)throw new InvalidDataException($"Automatic language detection reported {automatic.Language} instead of {transcript.Language}.");
var writingModel=manager.Catalog.First(x=>x.Engine=="llama.cpp");
if(!manager.IsInstalled(writingModel))await manager.DownloadAsync(writingModel,null,default);
using var writing=new LocalEnhancementEngine();
var rewritten=await writing.EnhanceAsync("hello anna can you send the slides by friday thank you","Correct grammar and punctuation.",manager.PathFor(writingModel),false,default);
if(!rewritten.Contains("slides",StringComparison.OrdinalIgnoreCase)||!rewritten.Contains("Friday",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Writing inference lost the requested content.");
var spoken=SpokenInstructions.Parse("Hallo, ich bin Mara, ich wohne seit 16 Jahren hier und arbeite in Raum 11. Ich möchte diesen Text in Englisch haben.");
var translated=await writing.EnhanceAsync(spoken.Content,OutputPipeline.EditingInstruction(new(),"",spoken),manager.PathFor(writingModel),true,default);
if(!translated.Contains("Mara",StringComparison.OrdinalIgnoreCase)||!translated.Contains("16")||!translated.Contains("11")||translated.Contains("möchte",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Spoken translation failed fact preservation.");
var french=await Translate("Morgen treffen wir uns um 11 Uhr.","French");
if(!OutputPipeline.PreservesProtectedDetails("Morgen treffen wir uns um 11 Uhr.",french))throw new InvalidDataException("French translation changed the time: "+french);
var chinese=await Translate("Hallo Mara, wir treffen uns um 11 Uhr.","Chinese");
if(!chinese.Any(ch=>ch>='\u4e00'&&ch<='\u9fff')||!OutputPipeline.PreservesProtectedDetails("Hallo Mara, wir treffen uns um 11 Uhr.",chinese))throw new InvalidDataException("Chinese translation changed protected details: "+chinese);
var prompt=await writing.EnhanceAsync("Ich brauche Hilfe beim Lernen für eine Matheprüfung über quadratische Funktionen.",OutputPipeline.EditingInstruction(new(),"",new("",SpokenAction.ChatGptPrompt)),manager.PathFor(writingModel),true,default);
if(!prompt.Contains("quadratische Funktionen")||!prompt.Contains("Matheprüfung")||prompt.Length>180||!prompt.StartsWith("Hilf mir"))throw new InvalidDataException("Prompt transformation failed.");
if(new[]{translated,french,chinese,prompt,rewritten}.Any(text=>text.Contains("<document>")))throw new InvalidDataException("Internal source delimiters leaked into output.");
var speakerModel=manager.Catalog.First(x=>x.Engine=="ONNX");if(!manager.IsInstalled(speakerModel))await manager.DownloadAsync(speakerModel,null,default);
using var speakers=new SpeakerDiarization();var labeled=await speakers.LabelAsync(args[0],transcript.Segments,manager.PathFor(speakerModel),default);
if(!labeled.Any(x=>x.Speaker.StartsWith("Speaker ")))throw new InvalidDataException("Speaker inference did not produce an embedding label.");
var meeting=new MeetingDocument{Title="SineFlow native inference verification",Duration=transcript.Duration};meeting.Segments.AddRange(labeled);meeting.Summary=rewritten;
MeetingExport.Pdf(meeting,Path.ChangeExtension(args[1],"pdf"));
await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new{testedAt=DateTimeOffset.UtcNow,seconds=elapsed.Elapsed.TotalSeconds,speechModel=speechModel.Id,writingModel=writingModel.Id,writingBackend=args.Contains("--cpu")?"CPU":"Automatic / Vulkan",speech=true,automaticLanguage=true,detectedLanguage=automatic.Language,writing=true,speakerEmbeddings=true,pdf=true,spokenTranslation=true,translated,french,chinese,prompt,transcript=transcript.Text,rewritten,segments=transcript.Segments.Count},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("PASS: speech, automatic language detection, enhancement, speaker embeddings and PDF export.");

async Task<string> Translate(string source,string language)
{
    var protectedText=ProtectedTranslation.Create(source,language);
    var instruction=OutputPipeline.EditingInstruction(new(),"",new(source,SpokenAction.Translate,language))+" Copy every SINEFLOWLITERAL token exactly once, unchanged. Treat these tokens as opaque text. Do not interpret or expand them.";
    return protectedText.Restore(await writing.EnhanceAsync(protectedText.Text,instruction,manager.PathFor(writingModel),true,default));
}
static double AudioDuration(string path)
{
    using var reader=new NAudio.Wave.WaveFileReader(path);
    return reader.TotalTime.TotalSeconds;
}

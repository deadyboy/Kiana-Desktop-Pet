using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;
namespace KianaPet {
 // Reads playback state from Windows SMTC (System Media Transport Controls).
 // Works with any music app that publishes a session (e.g. 汽水音乐), without
 // injecting into the player process. Poll("soda") restricts the source to
 // Soda Music only, making it a first-class source parallel to NetEase CDP.
 public sealed class SmtcMusic {
  static readonly string[] KnownApps=new[]{"soda","汽水","cloudmusic","网易云","qqmusic","qq音乐","kugou","酷狗","kuwo","酷我","spotify","applemusic","itunes"};
  GlobalSystemMediaTransportControlsSessionManager manager;
  GlobalSystemMediaTransportControlsSession session;string sessionApp="";
  string coverKey="";
  static Task<T> Op<T>(Windows.Foundation.IAsyncOperation<T> op){
   var done=new TaskCompletionSource<T>();
   op.Completed=delegate(Windows.Foundation.IAsyncOperation<T> info,Windows.Foundation.AsyncStatus status){if(status==Windows.Foundation.AsyncStatus.Completed)done.TrySetResult(info.GetResults());else done.TrySetException(new Exception("WinRT operation failed: "+status));};
   return done.Task;
  }
  public static string FriendlyName(string app){
   string low=(app??"").ToLowerInvariant();
   if(low.Contains("soda")||low.Contains("汽水"))return "汽水音乐";
   if(low.Contains("cloudmusic")||low.Contains("网易云"))return "网易云音乐";
   if(low.Contains("qqmusic")||low.Contains("qq音乐"))return "QQ 音乐";
   if(low.Contains("kugou")||low.Contains("酷狗"))return "酷狗音乐";
   if(low.Contains("kuwo")||low.Contains("酷我"))return "酷我音乐";
   if(low.Contains("spotify"))return "Spotify";
   if(low.Contains("applemusic")||low.Contains("itunes"))return "Apple Music";
   return string.IsNullOrEmpty(app)?"媒体应用":app;
  }
  async Task<GlobalSystemMediaTransportControlsSessionManager> Manager(){
   if(manager==null)manager=await Op(GlobalSystemMediaTransportControlsSessionManager.RequestAsync()).ConfigureAwait(false);
   return manager;
  }
  static bool Playing(GlobalSystemMediaTransportControlsSession s){
   try{return s.GetPlaybackInfo()!=null&&s.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;}catch{return false;}
  }
  GlobalSystemMediaTransportControlsSession Pick(string only){
   GlobalSystemMediaTransportControlsSession best=null;bool bestPlaying=false;
   foreach(var s in manager.GetSessions()){
    string low;try{low=(s.SourceAppUserModelId??"").ToLowerInvariant();}catch{continue;}
    if(only=="soda"){if(!low.Contains("soda")&&!low.Contains("汽水"))continue;}
    else if(!KnownApps.Any(k=>low.Contains(k)))continue;
    bool playing=Playing(s);
    if(best==null||playing&&!bestPlaying){best=s;bestPlaying=playing;try{sessionApp=s.SourceAppUserModelId??"";}catch{}}
   }
   return best;
  }
  public Task<MusicState> Poll(){return Poll(null);}
  public async Task<MusicState> Poll(string only){
   var next=new MusicState{Source="smtc",Label=only=="soda"?"未发现汽水音乐会话":"未发现汽水音乐等媒体会话"};
   await Manager().ConfigureAwait(false);
   var s=Pick(only);if(s==null){session=null;return next;}
   session=s;
   var props=await Op(s.TryGetMediaPropertiesAsync()).ConfigureAwait(false);
   var info=s.GetPlaybackInfo();
   next.Title=props.Title??"";next.Artist=props.Artist??"";
   if(next.Title.Length==0){next.Label=FriendlyName(sessionApp)+" · 等待播放";return next;}
   next.Connected=true;
   next.Playing=info!=null&&info.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
   if(info!=null){var c=info.Controls;next.CanToggle=next.Playing?c.IsPauseEnabled:c.IsPlayEnabled;next.CanNext=c.IsNextEnabled;next.CanPrevious=c.IsPreviousEnabled;}
   try{var tl=s.GetTimelineProperties();next.Duration=Math.Max(0,tl.EndTime.TotalSeconds);next.Position=Math.Max(0,tl.Position.TotalSeconds);if(next.Playing)next.Position=Math.Min(next.Duration>0?next.Duration:double.MaxValue,next.Position+Math.Max(0,(DateTimeOffset.UtcNow-tl.LastUpdatedTime).TotalSeconds));}catch{}
   next.Cover=await Cover(props).ConfigureAwait(false);
   next.At=DateTime.UtcNow;
   next.Label=FriendlyName(sessionApp)+"已连接 · "+(next.Playing?"正在播放":"已暂停");
   return next;
  }
  async Task<string> Cover(GlobalSystemMediaTransportControlsSessionMediaProperties props){
   string path=Path.Combine(Store.Root,"music-cover.png");
   try{
    if(props.Thumbnail==null)return "";
    string key=props.Title+"\n"+props.Artist;
    if(key==coverKey&&File.Exists(path))return path;
    var stream=await Op(props.Thumbnail.OpenReadAsync()).ConfigureAwait(false);
    using(stream){
     if(stream.Size<128||stream.Size>4*1024*1024)return "";
     var reader=new DataReader(stream);
     uint loaded=await Op(reader.LoadAsync((uint)stream.Size)).ConfigureAwait(false);
     if(loaded!=stream.Size)return "";
     var data=new byte[stream.Size];reader.ReadBytes(data);
     string tmp=path+".tmp";File.WriteAllBytes(tmp,data);File.Copy(tmp,path,true);File.Delete(tmp);
     coverKey=key;return path;
    }
   }catch{return File.Exists(path)?path:"";}
  }
  public async Task<bool> Command(string action){
   var s=session;if(s==null)return false;
   try{
    if(action=="toggle")return await Op(Playing(s)?s.TryPauseAsync():s.TryPlayAsync()).ConfigureAwait(false);
    if(action=="next")return await Op(s.TrySkipNextAsync()).ConfigureAwait(false);
    if(action=="previous")return await Op(s.TrySkipPreviousAsync()).ConfigureAwait(false);
   }catch{session=null;}
   return false;
  }
 }
}

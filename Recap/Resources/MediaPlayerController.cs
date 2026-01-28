using LibVLCSharp.Shared;
using System;
using System.Threading.Tasks;

namespace Recap
{
    public class MediaPlayerController : IDisposable
    {
        private readonly LibVLC _libVLC;
        private readonly MediaPlayer _mediaPlayer;
        
        public string ActiveVideoPath { get; private set; }
        public double VideoFps { get; private set; } = 1.0;
        public long Duration => _mediaPlayer.Length;
        public long Time 
        { 
            get => _mediaPlayer.Time; 
            set => _mediaPlayer.Time = value; 
        }

        public bool IsPlaying => _mediaPlayer.IsPlaying;
        public bool IsSeekable => _mediaPlayer.IsSeekable;
        public VLCState State => _mediaPlayer.State;

        public MediaPlayer Player => _mediaPlayer;

        public MediaPlayerController(LibVLC libVLC, MediaPlayer mediaPlayer)
        {
            _libVLC = libVLC;
            _mediaPlayer = mediaPlayer;
        }

        public void LoadVideo(string path)
        {
            if (ActiveVideoPath != path)
            {
                ActiveVideoPath = path;
                
                Task.Run(async () =>
                {
                    try
                    {
                        var analysis = await FFMpegCore.FFProbe.AnalyseAsync(path);
                        VideoFps = analysis.PrimaryVideoStream?.FrameRate ?? 1.0;
                    }
                    catch 
                    { 
                        VideoFps = 1.0; 
                    }
                });

                _mediaPlayer.Media = new Media(_libVLC, path, FromType.FromPath);
                _mediaPlayer.AspectRatio = null;       
                _mediaPlayer.Play();
            }
            if (VideoFps <= 0) VideoFps = 1.0;
        }

        public void Play()
        {
            _mediaPlayer.Play();
        }

        public void Pause()
        {
            _mediaPlayer.Pause();
        }

        public void Stop()
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Stop();
            }
        }

        public void Mute(bool mute)
        {
            _mediaPlayer.Mute = mute;
        }

        public void EnsurePlaying()
        {
            if (!_mediaPlayer.IsPlaying && _mediaPlayer.State != VLCState.Buffering)
            {
                _mediaPlayer.Mute = true;
                _mediaPlayer.Play();
            }
        }

        public void Dispose()
        {
        }
    }
}
